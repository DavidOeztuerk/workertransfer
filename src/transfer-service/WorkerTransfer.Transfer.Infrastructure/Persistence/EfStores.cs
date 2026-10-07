using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Transfer.Domain.Requests;
using WorkerTransfer.Transfer.Domain.Market;
using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Reads and writes <c>market_status</c>.</summary>
public sealed class EfMarketStore(TransferDbContext context) : IMarketStore
{
    /// <inheritdoc />
    public async Task<MarketStatus?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.MarketStatus
            .FirstOrDefaultAsync(candidate => candidate.Id == subject.Value, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        MarketStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        var existing = await context.MarketStatus
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == status.Subject.Value, cancellationToken);

        if (existing is null)
        {
            context.MarketStatus.Add(new MarketRow
            {
                Id = status.Subject.Value,
                Availability = Availabilities.Word(status.Availability),
                Employed = status.IsEmployed,
                Note = status.Remark,
                CreatedAt = status.CreatedAt.UtcDateTime,
                UpdatedAt = status.ChangedAt.UtcDateTime,
                OpenFrom = status.OpenFrom,
                OpenUntil = status.OpenUntil
            });

            return;
        }

        existing.Availability = Availabilities.Word(status.Availability);
        existing.Employed = status.IsEmployed;
        existing.Note = status.Remark;
        existing.UpdatedAt = status.ChangedAt.UtcDateTime;
        existing.OpenFrom = status.OpenFrom;
        existing.OpenUntil = status.OpenUntil;
    }

    private static MarketStatus ToAggregate(MarketRow row) =>
        MarketStatus.Restore(
            new SubjectId(row.Id),
            Availabilities.Read(row.Availability)
            ?? throw new InvalidOperationException(
                $"Unknown availability in the database: {row.Availability}"),
            row.Employed,
            row.Note,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.OpenFrom,
            row.OpenUntil);
}

/// <summary>Reads and writes <c>market_requests</c>.</summary>
public sealed class EfRequestStore(TransferDbContext context) : IRequestStore
{
    /// <inheritdoc />
    public async Task<MarketRequest?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Requests
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<MarketRequest?> GetAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Requests.FirstOrDefaultAsync(
            candidate => candidate.SubjectId == subject.Value && candidate.TenantId == company.Value,
            cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        MarketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.Requests
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == request.Id, cancellationToken);

        if (existing is null)
        {
            context.Requests.Add(new RequestRow
            {
                Id = request.Id,
                SubjectId = request.Subject.Value,
                TenantId = request.Company.Value,
                RequestedBy = request.RequestedBy?.Value,
                Status = RequestStates.Word(request.State),
                CreatedAt = request.CreatedAt.UtcDateTime,
                AnsweredAt = request.AnsweredAt?.UtcDateTime
            });

            return;
        }

        // Subject, company and asker do not stand here: they never change, and
        // an assignment that could change them would be the way to attribute a
        // request to somebody else after the fact.
        existing.Status = RequestStates.Word(request.State);
        existing.AnsweredAt = request.AnsweredAt?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRequest>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Requests
            .Where(candidate => candidate.SubjectId == subject.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRequest>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Requests
            .Where(candidate => candidate.TenantId == company.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    private static MarketRequest ToAggregate(RequestRow row) =>
        MarketRequest.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            row.RequestedBy is { } requestedBy ? new SubjectId(requestedBy) : null,
            RequestStates.Read(row.Status)
            ?? throw new InvalidOperationException(
                $"Unknown request state in the database: {row.Status}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } answeredAt
                ? new DateTimeOffset(answeredAt, TimeSpan.Zero)
                : null);
}

/// <summary>Reads and writes <c>transfers</c>.</summary>
public sealed class EfTransferStore(TransferDbContext context) : ITransferStore
{
    private static readonly string[] Running =
        [.. TransferStates.Running.Select(TransferStates.Word)];

    /// <inheritdoc />
    public async Task<Domain.Cases.Transfer?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Cases
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<Domain.Cases.Transfer?> GetCurrentAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Cases.FirstOrDefaultAsync(
            candidate => candidate.SubjectId == subject.Value
                        && candidate.TenantId == company.Value
                        && Running.Contains(candidate.Status),
            cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Domain.Cases.Transfer caseItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        var existing = await context.Cases
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == caseItem.Id, cancellationToken);

        if (existing is null)
        {
            var row = new CaseRow
            {
                Id = caseItem.Id,
                SubjectId = caseItem.Subject.Value,
                TenantId = caseItem.Company.Value,
                // Message and release requirement stand only here: both are
                // fixed at creation and never changed afterwards.
                RequiresRelease = caseItem.NeedsRelease,
                Message = caseItem.Message,
                CreatedAt = caseItem.CreatedAt.UtcDateTime
            };

            Apply(caseItem, row);
            context.Cases.Add(row);

            return;
        }

        Apply(caseItem, existing);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Cases.Transfer>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Cases
            .Where(candidate => candidate.SubjectId == subject.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Cases.Transfer>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Cases
            .Where(candidate => candidate.TenantId == company.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    private static void Apply(Domain.Cases.Transfer caseItem, CaseRow row)
    {
        row.Status = TransferStates.Word(caseItem.State);
        row.ReleaseConfirmed = caseItem.ReleaseConfirmed;
        row.OfferNote = caseItem.OfferText;
        row.OfferStartOn = caseItem.OfferStart;
        row.OfferFeeCents = caseItem.OfferFeeCents;
        row.UpdatedAt = caseItem.ChangedAt.UtcDateTime;
    }

    private static Domain.Cases.Transfer ToAggregate(CaseRow row) =>
        Domain.Cases.Transfer.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            TransferStates.Read(row.Status)
            ?? throw new InvalidOperationException(
                $"Unknown transfer state in the database: {row.Status}"),
            row.RequiresRelease,
            row.ReleaseConfirmed,
            row.Message,
            row.OfferNote,
            row.OfferStartOn,
            row.OfferFeeCents,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
}
