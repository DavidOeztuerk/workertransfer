using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>The saved searches in Postgres.</summary>
public sealed class EfSearchStore(ScoutDbContext context) : ISearchStore
{
    /// <inheritdoc />
    public Task AppendAsync(Search search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);

        context.Searches.Add(new SearchRow
        {
            Id = search.Id,
            TenantId = search.Company.Value,
            SubjectId = search.Subject.Value,
            Name = search.Name,
            Skills = [.. search.Filter.NamedWords],
            Location = search.Filter.Location,
            Remote = search.Filter.RemoteOnly,
            CreatedAt = search.CreatedAt.UtcDateTime
        });

        // No SaveChanges: the command runs in the transaction scope, and what is
        // not committed together there is committed separately — that is
        // something else.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Search>> ForHumanAsync(
        TenantId company, SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Searches
            .Where(row => row.TenantId == company.Value && row.SubjectId == subject.Value)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToDomain)];
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        Guid id, TenantId company, SubjectId subject, CancellationToken cancellationToken = default)
    {
        // AsTracking, because something is changed here: the context otherwise
        // reads untracked, `Remove` would get a detached row, and the caller
        // would still see a "done".
        var row = await context.Searches
            .AsTracking()
            .FirstOrDefaultAsync(
                entry => entry.Id == id
                           && entry.TenantId == company.Value
                           && entry.SubjectId == subject.Value,
                cancellationToken);

        if (row is null)
        {
            // "Not yours" and "does not exist" answer alike: a difference would
            // reveal that a colleague's search exists.
            return false;
        }

        context.Searches.Remove(row);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var searches = await context.Searches
            .AsTracking()
            .Where(row => row.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Searches.RemoveRange(searches);

        // And the notes ABOUT this human being. They carry their identifier and
        // are therefore personal — an erasure that takes away only one's own
        // searches would leave standing in every backup that this person
        // existed (ADR-0027).
        var notes = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(row => row.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(notes);

        // Always 0. This service knows no retention case: nothing stands here that
        // belongs to somebody else.
        return 0;
    }

    /// <summary>Builds the aggregate from the row.</summary>
    /// <remarks>
    /// Through <see cref="Search.Restore"/> and thus without checking again: a
    /// saved row was valid when it was created, and refusing it on reading would
    /// mean taking somebody's search away because a cap changed. It is
    /// canonicalised all the same — a new entry in the vocabulary thus reaches
    /// older rows too, without a data migration.
    /// </remarks>
    private static Search ToDomain(SearchRow row) =>
        Search.Restore(
            row.Id,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Name,
            SearchFilter.Restore(row.Skills, row.Location, row.Remote),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
}
