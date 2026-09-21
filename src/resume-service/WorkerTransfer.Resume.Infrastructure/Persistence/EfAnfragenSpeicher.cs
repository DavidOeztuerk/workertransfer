using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Anfragen;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Reads and writes <c>resume_requests</c>.</summary>
public sealed class EfRequestStore(ResumeDbContext context) : IRequestStore
{
    /// <inheritdoc />
    public async Task<ResumeRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Anfragen.SingleOrDefaultAsync(z => z.Id == id, cancellationToken);

        return row is null ? null : ZuDomaene(row);
    }

    /// <inheritdoc />
    public async Task<ResumeRequest?> FindeAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Anfragen.SingleOrDefaultAsync(
            z => z.SubjectId == subject.Value && z.TenantId == company.Value, cancellationToken);

        return row is null ? null : ZuDomaene(row);
    }

    /// <inheritdoc />
    public Task AppendAsync(ResumeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        context.Anfragen.Add(new RequestRow
        {
            Id = request.Id,
            SubjectId = request.Wer.Value,
            TenantId = request.Firma.Value,
            RequestedBy = request.Frager?.Value,
            Status = request.State,
            CreatedAt = request.Gestellt.UtcDateTime,
            AnsweredAt = request.Beantwortet?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ResumeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var row = await context.Anfragen
            .AsTracking()
            .SingleOrDefaultAsync(z => z.Id == request.Id, cancellationToken);

        if (row is null)
        {
            return;
        }

        // Only the two an answer may move. `requested_by` is not among them:
        // it is nulled by the erasure alone, and re-writing it here would put a
        // deleted person's id back (ADR-0027 §2).
        row.Status = request.State;
        row.AnsweredAt = request.Beantwortet?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequest>> ForPersonAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default) =>
        [.. (await context.Anfragen
                .Where(z => z.SubjectId == subject.Value)
                .OrderByDescending(z => z.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ZuDomaene)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumeRequest>> ForCompanyAsync(
        TenantId company,
        CancellationToken cancellationToken = default) =>
        [.. (await context.Anfragen
                .Where(z => z.TenantId == company.Value)
                .OrderByDescending(z => z.CreatedAt)
                .ToListAsync(cancellationToken))
            .Select(ZuDomaene)];

    private static ResumeRequest ZuDomaene(RequestRow row) =>
        ResumeRequest.Wiederherstellen(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            row.RequestedBy is { } frager ? new SubjectId(frager) : null,
            row.Status,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null);
}
