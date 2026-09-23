using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>The cases in Postgres.</summary>
public sealed class EfCaseStore(AssessmentDbContext context) : ICaseStore
{
    /// <inheritdoc />
    public async Task<Case?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Cases
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Case>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        [.. (await context.Cases
                .Where(entry => entry.SubjectId == subject.Value)
                .OrderByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ToAggregate)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Case>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        [.. (await context.Cases
                .Where(entry => entry.TenantId == company.Value)
                .OrderByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ToAggregate)];

    /// <inheritdoc />
    public async Task SaveAsync(Case caseItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        // AsTracking, because this changes the row. Without it the row would come
        // back detached, the assignments would go nowhere, and the caller would
        // still get its 200 — the defect `PUT /resumes/…/as-cv` once cost. The
        // insert path hides it, because `Add` always tracks; it would surface
        // only at the first submission.
        var row = await context.Cases
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == caseItem.Id, cancellationToken);

        if (row is null)
        {
            context.Cases.Add(ToRow(caseItem));
            return;
        }

        // Task, subject and company do not stand here: they never change.
        // Rewriting a set task afterwards would mean changing its size after
        // somebody has read it and agreed.
        row.SubmissionText = caseItem.Submission?.Text;
        row.SubmissionUrl = caseItem.Submission?.Url;
        row.SubmittedAt = caseItem.Submission?.At.UtcDateTime;
        row.EvaluationText = caseItem.Evaluation?.Text;
        row.EvaluationOutcome = caseItem.Evaluation is { } evaluation
            ? Words.Word(evaluation.Outcome)
            : null;
        row.EvaluatedAt = caseItem.Evaluation?.At.UtcDateTime;
        row.UpdatedAt = caseItem.ChangedAt.UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Cases <em>and</em> outbox rows. Whoever empties only the one table leaves
    /// notes behind carrying the id of a human being who no longer exists — and
    /// the promise of ADR-0027 is then half kept, without anybody noticing.
    /// </remarks>
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var cases = await context.Cases
            .AsTracking()
            .Where(entry => entry.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Cases.RemoveRange(cases);

        // And the notes ABOUT this human being. They carry their id and are
        // therefore personal data — an erasure that took only the cases would
        // leave standing in every backup that this person existed.
        var notes = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(entry => entry.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(notes);

        // No retention case: nothing here belongs to somebody else (ADR-0042,
        // ADR-0027 §3). An evaluation that survived the erasure would be exactly
        // the reference letter this service rules out.
        return 0;
    }

    private static CaseRow ToRow(Case caseItem) => new()
    {
        Id = caseItem.Id,
        SubjectId = caseItem.Subject.Value,
        TenantId = caseItem.Company.Value,
        Title = caseItem.Assignment.Title,
        Task = caseItem.Assignment.Text,
        Hours = caseItem.Assignment.Hours,
        DueAt = caseItem.Assignment.Deadline.UtcDateTime,
        SubmissionText = caseItem.Submission?.Text,
        SubmissionUrl = caseItem.Submission?.Url,
        SubmittedAt = caseItem.Submission?.At.UtcDateTime,
        EvaluationText = caseItem.Evaluation?.Text,
        EvaluationOutcome = caseItem.Evaluation is { } evaluation
            ? Words.Word(evaluation.Outcome)
            : null,
        EvaluatedAt = caseItem.Evaluation?.At.UtcDateTime,
        CreatedAt = caseItem.SetAt.UtcDateTime,
        UpdatedAt = caseItem.ChangedAt.UtcDateTime
    };

    /// <summary>Builds the aggregate from the row.</summary>
    /// <remarks>
    /// Through the <c>Restore</c> factories and therefore without checking again:
    /// a stored row was valid when it came into being, and refusing it on reading
    /// would mean taking somebody's case away because a limit has changed — and
    /// with it their evaluation, which they may always read.
    /// </remarks>
    private static Case ToAggregate(CaseRow row) =>
        Case.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            Assignment.Restore(
                row.Title, row.Task, row.Hours, At(row.DueAt)),
            row.SubmittedAt is { } submittedAt
                ? Submission.Restore(
                    row.SubmissionText ?? string.Empty, row.SubmissionUrl, At(submittedAt))
                : null,
            row.EvaluatedAt is { } evaluatedAt
                ? Evaluation.Restore(
                    row.EvaluationText ?? string.Empty,
                    Words.ReadOutcome(row.EvaluationOutcome) ?? Outcome.Rejected,
                    At(evaluatedAt))
                : null,
            At(row.CreatedAt),
            At(row.UpdatedAt));

    private static DateTimeOffset At(DateTime value) => new(value, TimeSpan.Zero);
}
