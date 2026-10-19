using Microsoft.EntityFrameworkCore;
using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Domain.Sources;

namespace WorkerTransfer.Jobs.Infrastructure.Persistence;

/// <summary>Reads and writes <c>job_sources</c>.</summary>
public sealed class EfJobSourceStore(JobsDbContext context) : IJobSourceStore
{
    /// <inheritdoc />
    public async Task<JobSource?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Sources.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobSource>> ForCompanyAsync(TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Sources
            .Where(entry => entry.TenantId == company.Value)
            .OrderBy(entry => entry.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobSource>> DueAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
    {
        var limit = before.UtcDateTime;
        var rows = await context.Sources
            .Where(entry => entry.ProvenAt != null && (entry.FetchedAt == null || entry.FetchedAt < limit))
            .OrderBy(entry => entry.FetchedAt)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DomainsAsync(CancellationToken cancellationToken = default) =>
        await context.Sources.Where(entry => entry.Domain != string.Empty)
            .Select(entry => entry.Domain).Distinct().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task SaveAsync(JobSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        // AsTracking, because this changes the row (CLAUDE.md, Persistence).
        var row = await context.Sources.AsTracking().FirstOrDefaultAsync(entry => entry.Id == source.Id, cancellationToken);

        if (row is null)
        {
            row = new JobSourceRow
            {
                Id = source.Id,
                TenantId = source.Company.Value,
                Kind = SourceWords.Word(source.Kind),
                Key = source.Key,
                Domain = source.Domain,
                Code = source.Code,
                CreatedAt = source.CreatedAt.UtcDateTime
            };
            context.Sources.Add(row);
        }

        row.Mode = SourceWords.Word(source.Mode);
        row.ProvenAt = source.ProvenAt?.UtcDateTime;
        row.FetchedAt = source.FetchedAt?.UtcDateTime;
        row.Outcome = source.Outcome;
    }

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Sources.Where(entry => entry.Id == id).ExecuteDeleteAsync(cancellationToken);

    private static JobSource ToAggregate(JobSourceRow row) =>
        JobSource.Restore(
            row.Id,
            new TenantId(row.TenantId),
            SourceWords.ReadKind(row.Kind) ?? throw new InvalidOperationException($"Unknown source kind: {row.Kind}"),
            row.Key,
            row.Domain,
            row.Code,
            SourceWords.ReadMode(row.Mode) ?? PublishMode.Draft,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.ProvenAt is { } proven ? new DateTimeOffset(proven, TimeSpan.Zero) : null,
            row.FetchedAt is { } at ? new DateTimeOffset(at, TimeSpan.Zero) : null,
            row.Outcome);
}
