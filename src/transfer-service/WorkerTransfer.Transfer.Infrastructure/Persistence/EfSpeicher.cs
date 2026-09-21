using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>market_status</c>.</summary>
public sealed class EfMarketStore(TransferDbContext context) : IMarketStore
{
    /// <inheritdoc />
    public async Task<MarketStatus?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.MarketStatus
            .FirstOrDefaultAsync(kandidat => kandidat.Id == subject.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        MarketStatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        var vorhanden = await context.MarketStatus
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == status.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            context.MarketStatus.Add(new MarketRow
            {
                Id = status.Wer.Value,
                Availability = Verfuegbarkeiten.Wort(status.Verfuegbarkeit),
                Employed = status.Beschaeftigt,
                Note = status.Notiz,
                CreatedAt = status.AngelegtAm.UtcDateTime,
                UpdatedAt = status.GeaendertAm.UtcDateTime
            });

            return;
        }

        vorhanden.Availability = Verfuegbarkeiten.Wort(status.Verfuegbarkeit);
        vorhanden.Employed = status.Beschaeftigt;
        vorhanden.Note = status.Notiz;
        vorhanden.UpdatedAt = status.GeaendertAm.UtcDateTime;
    }

    private static MarketStatus ZumAggregat(MarketRow row) =>
        MarketStatus.Stelle_her(
            new SubjectId(row.Id),
            Verfuegbarkeiten.Lies(row.Availability)
            ?? throw new InvalidOperationException(
                $"Unbekannte Verfügbarkeit in der Datenbank: {row.Availability}"),
            row.Employed,
            row.Note,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
}

/// <summary>Liest und schreibt <c>market_requests</c>.</summary>
public sealed class EfRequestStore(TransferDbContext context) : IRequestStore
{
    /// <inheritdoc />
    public async Task<MarketRequest?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Anfragen
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<MarketRequest?> GetAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Anfragen.FirstOrDefaultAsync(
            kandidat => kandidat.SubjectId == subject.Value && kandidat.TenantId == company.Value,
            cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        MarketRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vorhanden = await context.Anfragen
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == request.Id, cancellationToken);

        if (vorhanden is null)
        {
            context.Anfragen.Add(new RequestRow
            {
                Id = request.Id,
                SubjectId = request.Wer.Value,
                TenantId = request.Firma.Value,
                RequestedBy = request.Frager?.Value,
                Status = RequestStates.Wort(request.State),
                CreatedAt = request.AngelegtAm.UtcDateTime,
                AnsweredAt = request.BeantwortetAm?.UtcDateTime
            });

            return;
        }

        // Wer, Firma und Frager stehen hier nicht: sie ändern sich nie, und
        // eine Zuweisung, die es doch könnte, wäre der Weg, eine ResumeRequest
        // nachträglich jemand anderem zuzuschreiben.
        vorhanden.Status = RequestStates.Wort(request.State);
        vorhanden.AnsweredAt = request.BeantwortetAm?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRequest>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Anfragen
            .Where(kandidat => kandidat.SubjectId == subject.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRequest>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Anfragen
            .Where(kandidat => kandidat.TenantId == company.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    private static MarketRequest ZumAggregat(RequestRow row) =>
        MarketRequest.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            row.RequestedBy is { } frager ? new SubjectId(frager) : null,
            RequestStates.Lies(row.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Anfragestand in der Datenbank: {row.Status}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null);
}

/// <summary>Liest und schreibt <c>transfers</c>.</summary>
public sealed class EfTransferStore(TransferDbContext context) : ITransferStore
{
    private static readonly string[] Laufende =
        [.. Transferstaende.Laufende.Select(Transferstaende.Wort)];

    /// <inheritdoc />
    public async Task<Domain.Vorgaenge.Transfer?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Vorgaenge
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<Domain.Vorgaenge.Transfer?> GetCurrentAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var row = await context.Vorgaenge.FirstOrDefaultAsync(
            kandidat => kandidat.SubjectId == subject.Value
                        && kandidat.TenantId == company.Value
                        && Laufende.Contains(kandidat.Status),
            cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Domain.Vorgaenge.Transfer caseItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        var vorhanden = await context.Vorgaenge
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == caseItem.Id, cancellationToken);

        if (vorhanden is null)
        {
            var row = new CaseRow
            {
                Id = caseItem.Id,
                SubjectId = caseItem.Wer.Value,
                TenantId = caseItem.Firma.Value,
                // Nachricht und Freigabepflicht stehen nur hier: beide werden
                // beim Anlegen festgelegt und danach nie geändert.
                RequiresRelease = caseItem.BrauchtFreigabe,
                Message = caseItem.Nachricht,
                CreatedAt = caseItem.AngelegtAm.UtcDateTime
            };

            Uebertrage(caseItem, row);
            context.Vorgaenge.Add(row);

            return;
        }

        Uebertrage(caseItem, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Vorgaenge.Transfer>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Vorgaenge
            .Where(kandidat => kandidat.SubjectId == subject.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Vorgaenge.Transfer>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Vorgaenge
            .Where(kandidat => kandidat.TenantId == company.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    private static void Uebertrage(Domain.Vorgaenge.Transfer caseItem, CaseRow row)
    {
        row.Status = Transferstaende.Wort(caseItem.State);
        row.ReleaseConfirmed = caseItem.FreigabeBestaetigt;
        row.OfferNote = caseItem.Angebotstext;
        row.OfferStartOn = caseItem.Angebotsbeginn;
        row.OfferFeeCents = caseItem.AngebotsgebuehrCent;
        row.UpdatedAt = caseItem.GeaendertAm.UtcDateTime;
    }

    private static Domain.Vorgaenge.Transfer ZumAggregat(CaseRow row) =>
        Domain.Vorgaenge.Transfer.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            Transferstaende.Lies(row.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Transferstand in der Datenbank: {row.Status}"),
            row.RequiresRelease,
            row.ReleaseConfirmed,
            row.Message,
            row.OfferNote,
            row.OfferStartOn,
            row.OfferFeeCents,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
}
