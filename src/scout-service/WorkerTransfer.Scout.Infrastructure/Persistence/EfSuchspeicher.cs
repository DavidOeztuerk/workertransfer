using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Die gespeicherten Suchen in Postgres.</summary>
public sealed class EfSuchspeicher(ScoutDbContext kontext) : ISuchspeicher
{
    /// <inheritdoc />
    public Task FuegeHinzuAsync(Suche suche, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suche);

        kontext.Suchen.Add(new SucheZeile
        {
            Id = suche.Id,
            TenantId = suche.Firma.Value,
            SubjectId = suche.Wer.Value,
            Name = suche.Name,
            Skills = [.. suche.Filter.GenannteWorte],
            Location = suche.Filter.Ort,
            Remote = suche.Filter.NurRemote,
            CreatedAt = suche.AngelegtAm.UtcDateTime
        });

        // Ohne SaveChanges: der Befehl läuft in der Transaktionsklammer, und
        // was dort nicht gemeinsam festgeschrieben wird, ist einzeln
        // festgeschrieben — das ist etwas anderes.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Suche>> FuerMenschAsync(
        TenantId firma, SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Suchen
            .Where(zeile => zeile.TenantId == firma.Value && zeile.SubjectId == wer.Value)
            .OrderByDescending(zeile => zeile.CreatedAt)
            .ThenByDescending(zeile => zeile.Id)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZurDomaene)];
    }

    /// <inheritdoc />
    public async Task<bool> EntferneAsync(
        Guid id, TenantId firma, SubjectId wer, CancellationToken cancellationToken = default)
    {
        // AsTracking, weil hier geändert wird: der Kontext liest sonst
        // unverfolgt, `Remove` bekäme eine losgelöste Zeile, und der Aufrufer
        // sähe trotzdem ein „erledigt".
        var zeile = await kontext.Suchen
            .AsTracking()
            .FirstOrDefaultAsync(
                eintrag => eintrag.Id == id
                           && eintrag.TenantId == firma.Value
                           && eintrag.SubjectId == wer.Value,
                cancellationToken);

        if (zeile is null)
        {
            // „Gehört dir nicht" und „gibt es nicht" antworten gleich: ein
            // Unterschied verriete, dass die Suche eines Kollegen existiert.
            return false;
        }

        kontext.Suchen.Remove(zeile);

        return true;
    }

    /// <inheritdoc />
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var suchen = await kontext.Suchen
            .AsTracking()
            .Where(zeile => zeile.SubjectId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Suchen.RemoveRange(suchen);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die eigenen
        // Suchen wegnimmt, liesse in jeder Sicherung stehen, dass es diese
        // Person gab (ADR-0027).
        var vermerke = await kontext.Set<OutboxZeile>()
            .AsTracking()
            .Where(zeile => zeile.UserId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Set<OutboxZeile>().RemoveRange(vermerke);

        // Immer 0. Dieser Dienst kennt keinen Aufbewahrungsfall: hier steht
        // nichts, was jemand anderem gehört.
        return 0;
    }

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Suche.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem seine Suche zu entziehen, weil sich eine
    /// Obergrenze geändert hat. Kanonisiert wird trotzdem — ein neuer Eintrag
    /// im Wortschatz wirkt so auch auf ältere Zeilen, ohne Datenwanderung.
    /// </remarks>
    private static Suche ZurDomaene(SucheZeile zeile) =>
        Suche.Stelle_her(
            zeile.Id,
            new TenantId(zeile.TenantId),
            new SubjectId(zeile.SubjectId),
            zeile.Name,
            Suchfilter.Stelle_her(zeile.Skills, zeile.Location, zeile.Remote),
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero));
}
