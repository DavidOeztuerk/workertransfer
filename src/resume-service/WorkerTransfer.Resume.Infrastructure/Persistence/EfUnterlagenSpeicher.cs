using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Die Unterlagen einer Person, ohne ihre Bytes.</summary>
/// <remarks>
/// Wie überall in diesem Baum kommen die Aggregate <strong>abgelöst</strong>
/// zurück: eine Änderung erreicht die Datenbank nur über ein ausdrückliches
/// Speichern. Das Vergessen kostet im Test nichts und verliert in Produktion
/// den Schreibvorgang.
/// </remarks>
public sealed class EfUnterlagenSpeicher(ResumeDbContext kontext) : IUnterlagenSpeicher
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Unterlage>> AlleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.Unterlagen
            .AsNoTracking()
            .Where(zeile => zeile.SubjectId == wer.Value)
            .OrderByDescending(zeile => zeile.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<Unterlage?> HoleAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Unterlagen
            .AsNoTracking()
            .FirstOrDefaultAsync(
                eintrag => eintrag.Id == id && eintrag.SubjectId == wer.Value,
                cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Unterlage>> HoleVieleAsync(
        SubjectId wer, IReadOnlyList<Guid> kennungen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kennungen);

        if (kennungen.Count == 0)
        {
            return [];
        }

        var zeilen = await kontext.Unterlagen
            .AsNoTracking()
            .Where(zeile => zeile.SubjectId == wer.Value && kennungen.Contains(zeile.Id))
            .ToListAsync(cancellationToken);

        // In der REIHENFOLGE DER FRAGE, nicht in der der Datenbank: die Mappe
        // zeigt die Reiter so, wie die Person sie beigelegt hat.
        var nachKennung = zeilen.ToDictionary(zeile => zeile.Id);

        return
        [
            .. kennungen
                .Where(nachKennung.ContainsKey)
                .Select(kennung => ZumAggregat(nachKennung[kennung]))
        ];
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Unterlage unterlage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unterlage);

        // `AsTracking()` IST DER SCHREIBVORGANG, nicht eine Feinheit.
        //
        // Der ganze Kontext faehrt `QueryTrackingBehavior.NoTracking`
        // (`ResumeDbContextFactory`), und ohne diese Zeile kommt die Zeile
        // ABGELOEST zurueck: die Zuweisungen unten laufen ins Leere, `SaveChanges`
        // sieht nichts, und der Aufrufer bekommt trotzdem seine 204. Gemessen am
        // 09.09.2026 an `PUT /resumes/me/documents/{id}/as-cv` — die Antwort war
        // 204, die Art blieb `sonstiges`.
        //
        // Der Anlegepfad blieb davon unberuehrt, weil `Add` immer verfolgt. Genau
        // deshalb faellt so etwas erst beim ersten AENDERNDEN Aufrufer auf, und
        // dieser Speicher hatte bis dahin keinen. Alle achtzehn anderen Speicher
        // im Baum rufen `AsTracking()`; dieser war der einzige ohne.
        var vorhanden = await kontext.Unterlagen
            .AsTracking()
            .FirstOrDefaultAsync(zeile => zeile.Id == unterlage.Id, cancellationToken);

        if (vorhanden is null)
        {
            vorhanden = new UnterlageZeile { Id = unterlage.Id };
            kontext.Unterlagen.Add(vorhanden);
        }

        vorhanden.SubjectId = unterlage.Wer.Value;
        vorhanden.Name = unterlage.Name;
        vorhanden.Kind = unterlage.Art;
        vorhanden.ContentType = unterlage.Inhaltstyp;
        vorhanden.SizeBytes = unterlage.Groesse;
        vorhanden.StorageKey = unterlage.Ablageschluessel;
        vorhanden.CreatedAt = unterlage.Hochgeladen.UtcDateTime;
    }

    /// <inheritdoc />
    public Task LoescheAsync(
        SubjectId wer, Guid id, CancellationToken cancellationToken = default) =>
        kontext.Unterlagen
            .Where(zeile => zeile.Id == id && zeile.SubjectId == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static Unterlage ZumAggregat(UnterlageZeile zeile) =>
        Unterlage.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            zeile.Name,
            zeile.Kind,
            zeile.ContentType,
            zeile.SizeBytes,
            zeile.StorageKey,
            new DateTimeOffset(zeile.CreatedAt, TimeSpan.Zero));
}
