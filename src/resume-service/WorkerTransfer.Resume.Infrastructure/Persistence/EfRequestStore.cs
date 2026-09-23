using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Reads and writes <c>resume_requests</c>.</summary>
public sealed class EfRequestStore(ResumeDbContext context) : IRequestStore
{
    /// <inheritdoc />
    public async Task<ResumeRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Requests.SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task<ResumeRequest?> FindAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Requests.SingleOrDefaultAsync(
            r => r.SubjectId == subject.Value && r.TenantId == company.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public Task AppendAsync(ResumeRequest requestRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestRecord);

        context.Requests.Add(new RequestRow
        {
            Id = requestRecord.Id,
            SubjectId = requestRecord.Subject.Value,
            TenantId = requestRecord.Company.Value,
            RequestedBy = requestRecord.AskedBy?.Value,
            Status = requestRecord.State,
            CreatedAt = requestRecord.AskedAt.UtcDateTime,
            AnsweredAt = requestRecord.AnsweredAt?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ResumeRequest requestRecord, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestRecord);

        var row = await context.Requests
            .AsTracking()
            .SingleOrDefaultAsync(r => r.Id == requestRecord.Id, cancellationToken);

        if (row is null)
        {
            return;
        }

        // Only the two an answer may move. `requested_by` is not among them:
        // it is nulled by the erasure alone, and re-writing it here would put a
        // deleted person's id back (ADR-0027 §2).
        row.Status = requestRecord.State;
        row.AnsweredAt = requestRecord.AnsweredAt?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequest>> ForPersonAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default) =>
        [.. (await context.Requests
                .Where(r => r.SubjectId == subject.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ToDomain)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequest>> ForCompanyAsync(
        TenantId company,
        CancellationToken cancellationToken = default) =>
        [.. (await context.Requests
                .Where(r => r.TenantId == company.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ToDomain)];

    private static ResumeRequest ToDomain(RequestRow row) =>
        ResumeRequest.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            row.RequestedBy is { } askedBy ? new SubjectId(askedBy) : null,
            row.Status,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } answeredAt
                ? new DateTimeOffset(answeredAt, TimeSpan.Zero)
                : null);
}
