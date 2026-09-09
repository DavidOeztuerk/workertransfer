using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;
using WorkerTransfer.Transfer.Domain.Vorgaenge;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>market_status</c>.</summary>
public sealed class EfMarktspeicher(TransferDbContext kontext) : IMarktspeicher
{
    /// <inheritdoc />
    public async Task<Marktstatus?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Marktstatus
            .FirstOrDefaultAsync(kandidat => kandidat.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Marktstatus status, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        var vorhanden = await kontext.Marktstatus
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == status.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Marktstatus.Add(new MarktZeile
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

    private static Marktstatus ZumAggregat(MarktZeile zeile) =>
        Marktstatus.Stelle_her(
            new SubjectId(zeile.Id),
            Verfuegbarkeiten.Lies(zeile.Availability)
            ?? throw new InvalidOperationException(
                $"Unbekannte Verfügbarkeit in der Datenbank: {zeile.Availability}"),
            zeile.Employed,
            zeile.Note,
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
}

/// <summary>Liest und schreibt <c>market_requests</c>.</summary>
public sealed class EfAnfragenspeicher(TransferDbContext kontext) : IAnfragenspeicher
{
    /// <inheritdoc />
    public async Task<Marktanfrage?> HoleAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Anfragen
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<Marktanfrage?> HoleAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Anfragen.FirstOrDefaultAsync(
            kandidat => kandidat.SubjectId == wer.Value && kandidat.TenantId == firma.Value,
            cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Marktanfrage anfrage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anfrage);

        var vorhanden = await kontext.Anfragen
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == anfrage.Id, cancellationToken);

        if (vorhanden is null)
        {
            kontext.Anfragen.Add(new AnfrageZeile
            {
                Id = anfrage.Id,
                SubjectId = anfrage.Wer.Value,
                TenantId = anfrage.Firma.Value,
                RequestedBy = anfrage.Frager?.Value,
                Status = Anfragestaende.Wort(anfrage.Stand),
                CreatedAt = anfrage.AngelegtAm.UtcDateTime,
                AnsweredAt = anfrage.BeantwortetAm?.UtcDateTime
            });

            return;
        }

        // Wer, Firma und Frager stehen hier nicht: sie ändern sich nie, und
        // eine Zuweisung, die es doch könnte, wäre der Weg, eine Anfrage
        // nachträglich jemand anderem zuzuschreiben.
        vorhanden.Status = Anfragestaende.Wort(anfrage.Stand);
        vorhanden.AnsweredAt = anfrage.BeantwortetAm?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Marktanfrage>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Anfragen
            .Where(kandidat => kandidat.SubjectId == wer.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Marktanfrage>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Anfragen
            .Where(kandidat => kandidat.TenantId == firma.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    private static Marktanfrage ZumAggregat(AnfrageZeile zeile) =>
        Marktanfrage.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            new TenantId(zeile.TenantId),
            zeile.RequestedBy is { } frager ? new SubjectId(frager) : null,
            Anfragestaende.Lies(zeile.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Anfragestand in der Datenbank: {zeile.Status}"),
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            zeile.AnsweredAt is { } beantwortet
                ? new DateTimeOffset(beantwortet, TimeSpan.Zero)
                : null);
}

/// <summary>Liest und schreibt <c>transfers</c>.</summary>
public sealed class EfTransferspeicher(TransferDbContext kontext) : ITransferspeicher
{
    private static readonly string[] Laufende =
        [.. Transferstaende.Laufende.Select(Transferstaende.Wort)];

    /// <inheritdoc />
    public async Task<Domain.Vorgaenge.Transfer?> HoleAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Vorgaenge
            .FirstOrDefaultAsync(kandidat => kandidat.Id == id, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<Domain.Vorgaenge.Transfer?> HoleLaufendenAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Vorgaenge.FirstOrDefaultAsync(
            kandidat => kandidat.SubjectId == wer.Value
                        && kandidat.TenantId == firma.Value
                        && Laufende.Contains(kandidat.Status),
            cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Domain.Vorgaenge.Transfer vorgang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vorgang);

        var vorhanden = await kontext.Vorgaenge
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == vorgang.Id, cancellationToken);

        if (vorhanden is null)
        {
            var zeile = new VorgangsZeile
            {
                Id = vorgang.Id,
                SubjectId = vorgang.Wer.Value,
                TenantId = vorgang.Firma.Value,
                // Nachricht und Freigabepflicht stehen nur hier: beide werden
                // beim Anlegen festgelegt und danach nie geändert.
                RequiresRelease = vorgang.BrauchtFreigabe,
                Message = vorgang.Nachricht,
                CreatedAt = vorgang.AngelegtAm.UtcDateTime
            };

            Uebertrage(vorgang, zeile);
            kontext.Vorgaenge.Add(zeile);

            return;
        }

        Uebertrage(vorgang, vorhanden);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Vorgaenge.Transfer>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Vorgaenge
            .Where(kandidat => kandidat.SubjectId == wer.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Domain.Vorgaenge.Transfer>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Vorgaenge
            .Where(kandidat => kandidat.TenantId == firma.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    private static void Uebertrage(Domain.Vorgaenge.Transfer vorgang, VorgangsZeile zeile)
    {
        zeile.Status = Transferstaende.Wort(vorgang.Stand);
        zeile.ReleaseConfirmed = vorgang.FreigabeBestaetigt;
        zeile.OfferNote = vorgang.Angebotstext;
        zeile.OfferStartOn = vorgang.Angebotsbeginn;
        zeile.OfferFeeCents = vorgang.AngebotsgebuehrCent;
        zeile.UpdatedAt = vorgang.GeaendertAm.UtcDateTime;
    }

    private static Domain.Vorgaenge.Transfer ZumAggregat(VorgangsZeile zeile) =>
        Domain.Vorgaenge.Transfer.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            new TenantId(zeile.TenantId),
            Transferstaende.Lies(zeile.Status)
            ?? throw new InvalidOperationException(
                $"Unbekannter Transferstand in der Datenbank: {zeile.Status}"),
            zeile.RequiresRelease,
            zeile.ReleaseConfirmed,
            zeile.Message,
            zeile.OfferNote,
            zeile.OfferStartOn,
            zeile.OfferFeeCents,
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
}
