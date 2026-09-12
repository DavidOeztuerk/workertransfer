using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Die Gespräche in Postgres.</summary>
public sealed class EfGespraechsspeicher(AdvisorDbContext kontext) : IGespraechsspeicher
{
    /// <inheritdoc />
    public async Task<Gespraech?> HoleAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Gespraeche
            .FirstOrDefaultAsync(eintrag => eintrag.Id == id, cancellationToken);

        return zeile is null ? null : ZurDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task<Gespraech?> HoleLaufendesAsync(
        SubjectId wer, TenantId firma, CancellationToken cancellationToken = default)
    {
        var laufende = Gespraechsstaende.Laufende.Select(Gespraechsstaende.Wort).ToArray();

        var zeile = await kontext.Gespraeche
            .Where(eintrag => eintrag.SubjectId == wer.Value
                              && eintrag.TenantId == firma.Value
                              && laufende.Contains(eintrag.State))
            .OrderByDescending(eintrag => eintrag.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return zeile is null ? null : ZurDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Gespraech gespraech, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gespraech);

        // AsTracking, weil hier geändert wird: ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200.
        var zeile = await kontext.Gespraeche
            .AsTracking()
            .FirstOrDefaultAsync(eintrag => eintrag.Id == gespraech.Id, cancellationToken);

        if (zeile is null)
        {
            kontext.Gespraeche.Add(new GespraechsZeile
            {
                Id = gespraech.Id,
                SubjectId = gespraech.Wer.Value,
                TenantId = gespraech.Firma.Value,
                State = Gespraechsstaende.Wort(gespraech.Stand),
                Note = gespraech.Anlass,
                OpenedAt = gespraech.EroeffnetAm.UtcDateTime,
                UpdatedAt = gespraech.GeaendertAm.UtcDateTime
            });

            return;
        }

        zeile.State = Gespraechsstaende.Wort(gespraech.Stand);
        zeile.UpdatedAt = gespraech.GeaendertAm.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Gespraech>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Gespraeche
            .Where(eintrag => eintrag.SubjectId == wer.Value)
            .OrderByDescending(eintrag => eintrag.UpdatedAt)
            .ThenByDescending(eintrag => eintrag.Id)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Gespraech>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Gespraeche
            .Where(eintrag => eintrag.TenantId == firma.Value)
            .OrderByDescending(eintrag => eintrag.UpdatedAt)
            .ThenByDescending(eintrag => eintrag.Id)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var gespraeche = await kontext.Gespraeche
            .AsTracking()
            .Where(eintrag => eintrag.SubjectId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Gespraeche.RemoveRange(gespraeche);

        var mandate = await kontext.Mandate
            .AsTracking()
            .Where(eintrag => eintrag.Id == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Mandate.RemoveRange(mandate);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die Gespräche
        // wegnimmt, liesse in jeder Sicherung stehen, dass es diese Person gab
        // (ADR-0027).
        var vermerke = await kontext.Set<OutboxZeile>()
            .AsTracking()
            .Where(eintrag => eintrag.UserId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Set<OutboxZeile>().RemoveRange(vermerke);

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: ein Gespräch
        // ist eine Beziehung, kein Beleg, und ein Mandat gehört der Person.
        return 0;
    }

    private static Gespraech ZurDomaene(GespraechsZeile zeile) =>
        Gespraech.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            new TenantId(zeile.TenantId),
            Stand(zeile.State),
            zeile.Note,
            new DateTimeOffset(zeile.OpenedAt, TimeSpan.Zero),
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));

    /// <summary>Das Wort zurück in einen Stand.</summary>
    /// <remarks>
    /// Ein unbekanntes Wort ist ein Programmierfehler und wird laut: still auf
    /// „laufend" zu fallen hiesse, ein beendetes Gespräch wieder zu öffnen.
    /// </remarks>
    private static Gespraechsstand Stand(string wort) => wort switch
    {
        "talking" => Gespraechsstand.Laufend,
        "agreed" => Gespraechsstand.Zugestimmt,
        "handed_over" => Gespraechsstand.Uebergeben,
        "ended" => Gespraechsstand.Beendet,
        _ => throw new InvalidOperationException($"'{wort}' is not a conversation state.")
    };
}
