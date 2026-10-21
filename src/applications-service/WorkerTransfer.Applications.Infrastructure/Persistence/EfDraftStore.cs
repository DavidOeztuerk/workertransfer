using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>The drafts and their comments.</summary>
/// <remarks>
/// As everywhere here the aggregates come back <strong>detached</strong>: a
/// change reaches the database only through an explicit save.
/// </remarks>
public sealed class EfDraftStore(ApplicationsDbContext context) : IDraftStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ApplicationDraft>> MineAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Drafts
            .Where(row => row.SubjectId == subject.Value)
            .OrderByDescending(row => row.UpdatedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return [];
        }

        // ONE query for all comments, not one per draft: a list with twenty
        // drafts would otherwise cost twenty-one round trips.
        var ids = rows.Select(row => row.Id).ToList();
        var comments = await context.Comments
            .Where(row => ids.Contains(row.DraftId))
            .ToListAsync(cancellationToken);

        var byDraft = comments
            .GroupBy(row => row.DraftId)
            .ToDictionary(group => group.Key, group => group.ToList());

        return
        [
            .. rows.Select(row => ToAggregate(
                row,
                byDraft.TryGetValue(row.Id, out var own) ? own : []))
        ];
    }

    /// <inheritdoc />
    public async Task<ApplicationDraft?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Drafts
            .FirstOrDefaultAsync(
                entry => entry.Id == id && entry.SubjectId == subject.Value,
                cancellationToken);

        if (row is null)
        {
            return null;
        }

        var comments = await context.Comments
            .Where(entry => entry.DraftId == id)
            .ToListAsync(cancellationToken);

        return ToAggregate(row, comments);
    }

    /// <inheritdoc />
    public async Task<ApplicationDraft?> OpenOneAsync(
        SubjectId subject, Guid job, CancellationToken cancellationToken = default)
    {
        var row = await context.Drafts
            .Where(entry => entry.SubjectId == subject.Value
                && entry.JobId == job
                && entry.Status != StateWord(DraftState.Sent))
            .OrderByDescending(entry => entry.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        var comments = await context.Comments
            .Where(entry => entry.DraftId == row.Id)
            .ToListAsync(cancellationToken);

        return ToAggregate(row, comments);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        ApplicationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var row = await context.Drafts
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == draft.Id, cancellationToken);

        if (row is null)
        {
            row = new DraftRow { Id = draft.Id, CreatedAt = draft.CreatedAt.UtcDateTime };
            context.Drafts.Add(row);
        }

        row.JobId = draft.Job;
        row.TenantId = draft.Company.Value;
        row.SubjectId = draft.Subject.Value;
        row.Subject = draft.SubjectLine;
        row.Body = draft.Text;
        row.Status = StateWord(draft.State);
        row.Version = draft.Version;
        row.Error = draft.Error;
        row.SharesResume = draft.SharesResume;
        row.Documents = JsonSerializer.Serialize(draft.Documents);
        row.UpdatedAt = draft.UpdatedAt.UtcDateTime;
        row.WritingStartedAt = draft.WritingStartedAt?.UtcDateTime;

        // The comments: create new ones, carry over ticked-off ones. None is
        // deleted — a comment is the evidence that somebody objected, and
        // letting it disappear would rob the version history of its reason.
        var existing = await context.Comments
            .AsTracking()
            .Where(entry => entry.DraftId == draft.Id)
            .ToListAsync(cancellationToken);

        foreach (var comment in draft.Comments)
        {
            var found = existing.FirstOrDefault(entry => entry.Id == comment.Id);

            if (found is null)
            {
                context.Comments.Add(new CommentRow
                {
                    Id = comment.Id,
                    DraftId = draft.Id,
                    Body = comment.Text,
                    Quote = comment.Passage,
                    Resolved = comment.Resolved,
                    CreatedAt = comment.CreatedAt.UtcDateTime
                });

                continue;
            }

            found.Resolved = comment.Resolved;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        await context.Comments
            .Where(row => row.DraftId == id)
            .ExecuteDeleteAsync(cancellationToken);

        await context.Drafts
            .Where(row => row.Id == id && row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>The state as a word — snake_case, as everywhere on the wire.</summary>
    /// <remarks>
    /// The same word stands in the column <c>application_drafts.status</c> and in
    /// the field <c>status</c> of <c>DraftV1</c>; the interface checks the words
    /// one by one. They are spelt out here, never derived from the member names.
    /// </remarks>
    public static string StateWord(DraftState state) => state switch
    {
        DraftState.Open => "open",
        DraftState.Generating => "generating",
        DraftState.Review => "review",
        DraftState.NeedsChanges => "needs_changes",
        DraftState.Approved => "approved",
        DraftState.Sent => "sent",
        _ => "failed"
    };

    /// <summary>The word as a state.</summary>
    public static DraftState ParseState(string? word) => word switch
    {
        "open" => DraftState.Open,
        "review" => DraftState.Review,
        "needs_changes" => DraftState.NeedsChanges,
        "approved" => DraftState.Approved,
        "sent" => DraftState.Sent,
        "failed" => DraftState.Failed,
        _ => DraftState.Generating
    };

    private static ApplicationDraft ToAggregate(
        DraftRow row, IReadOnlyList<CommentRow> comments) =>
        ApplicationDraft.Restore(
            row.Id,
            row.JobId,
            new TenantId(row.TenantId),
            new SubjectId(row.SubjectId),
            row.Subject,
            row.Body,
            ParseState(row.Status),
            row.Version,
            row.Error,
            row.SharesResume,
            JsonSerializer.Deserialize<List<Guid>>(row.Documents) ?? [],
            [
                .. comments.Select(entry => Comment.Restore(
                    entry.Id, entry.Body, entry.Quote, entry.Resolved,
                    new DateTimeOffset(entry.CreatedAt, TimeSpan.Zero)))
            ],
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.WritingStartedAt is { } start
                ? new DateTimeOffset(start, TimeSpan.Zero)
                : null);
}
