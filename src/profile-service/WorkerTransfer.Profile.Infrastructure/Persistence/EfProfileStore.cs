using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Domain.Skills;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>The profiles in Postgres.</summary>
public sealed class EfProfileStore(ProfileDbContext context) : IProfileStore
{
    /// <inheritdoc />
    public async Task<PersonProfile?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Profiles
            .FirstOrDefaultAsync(column => column.Id == subject.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task StoreAsync(PersonProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var row = await context.Profiles
            .AsTracking()
            .FirstOrDefaultAsync(column => column.Id == profile.Subject.Value, cancellationToken);

        if (row is null)
        {
            context.Profiles.Add(new ProfileRow
            {
                Id = profile.Subject.Value,
                Headline = profile.Headline,
                Text = profile.Text,
                Location = profile.Location,
                RemoteOk = profile.RemoteOk,
                Skills = [.. profile.Skills.Values],
                Commute = profile.Commute,
                Relocation = profile.Relocation,
                CreatedAt = profile.CreatedAt,
                UpdatedAt = profile.UpdatedAt
            });

            return;
        }

        row.Headline = profile.Headline;
        row.Text = profile.Text;
        row.Location = profile.Location;
        row.RemoteOk = profile.RemoteOk;
        row.Skills = [.. profile.Skills.Values];
        row.Commute = profile.Commute;
        row.Relocation = profile.Relocation;
        row.UpdatedAt = profile.UpdatedAt;
    }

    /// <inheritdoc />
    public async Task<ProfilePage> PageAsync(
        PageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = context.Profiles
            .OrderByDescending(column => column.UpdatedAt)
            .ThenByDescending(column => column.Id)
            .AsQueryable();

        // Upper and lower case do not matter, because the skill list already
        // deduplicates that way when saving: "Python" and "python" are the same
        // skill there, and a search that told them apart would contradict the
        // service's own data keeping.
        //
        // Compared at query time instead of over a mirrored lower-cased column:
        // that would be a second copy of the same data. If it gets tight, a GIN
        // index over exactly this expression is the answer — not a second
        // column.
        var wanted = (request.Skills ?? [])
            .Select(skill => Vocabulary.Canonicalise(skill).ToUpperInvariant())
            .ToArray();

        // OR: at least one of the words suffices. A hit may therefore NOT name
        // something — and exactly that turns scout-service's tick list into
        // information in the first place (ADR-0036). The AND of `/candidates`
        // fell with that route.
        if (wanted.Length > 0)
        {
            query = query.Where(column =>
                column.Skills.Any(entry => wanted.Contains(entry.ToUpper())));
        }

        if (request.Location.Length > 0)
        {
            var pattern = $"%{request.Location.Trim()}%";
            query = query.Where(column => EF.Functions.ILike(column.Location, pattern));
        }

        if (request.RemoteOnly)
        {
            // In one direction only: `remote_ok = false` means "has not said
            // yes", not "refuses". A filter on it would exclude people who
            // simply ticked nothing.
            query = query.Where(column => column.RemoteOk);
        }

        if (request.After is { } after)
        {
            query = query.Where(column =>
                column.UpdatedAt < after.UpdatedAt
                || (column.UpdatedAt == after.UpdatedAt && column.Id < after.Subject.Value));
        }

        // Fetch one row more than is shown: that way it is settled without a
        // second query whether it continues — and without a COUNT that would
        // name the total nobody may name here.
        var rows = await query.Take(request.PageLength + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > request.PageLength;

        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var last = rows.Count > 0 ? rows[^1] : null;

        return new ProfilePage(
            [.. rows.Select(ToDomain)],
            hasMore && last is not null
                ? new PageCursor(last.UpdatedAt, new SubjectId(last.Id))
                : null);
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        // Without SaveChanges: the erasure command runs in the transaction
        // scope, and the audit trail must commit with it or not at all.
        var row = await context.Profiles
            .AsTracking()
            .FirstOrDefaultAsync(column => column.Id == subject.Value, cancellationToken);

        if (row is not null)
        {
            context.Profiles.Remove(row);
        }

        // Always 0. This service knows no retention case: there is nothing here
        // that belongs to somebody else.
        return 0;
    }

    /// <summary>Builds the aggregate from the row.</summary>
    /// <remarks>
    /// Through <see cref="PersonProfile.Restore"/> and therefore without a
    /// renewed check: a stored row was valid when it came into being, and
    /// refusing it on reading would mean taking somebody's profile away because
    /// an upper limit has changed.
    /// <para>
    /// The skills run through <see cref="SkillList"/> anyway: that is the only
    /// entrance that builds the list, and a new entry in the vocabulary then
    /// also takes effect on rows written before it — without a data migration.
    /// </para>
    /// </remarks>
    private static PersonProfile ToDomain(ProfileRow row) =>
        PersonProfile.Restore(
            new SubjectId(row.Id),
            row.Headline,
            row.Text,
            row.Location,
            row.RemoteOk,
            SkillList.Restore(row.Skills),
            row.CreatedAt,
            row.UpdatedAt,
            row.Commute,
            row.Relocation);
}
