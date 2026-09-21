using Noelia.Core.Identity;
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
public sealed class EfDocumentRecordStore(ResumeDbContext context) : IDocumentRecordStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecord>> AllAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Unterlagen
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value)
            .OrderByDescending(row => row.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public async Task<DocumentRecord?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Unterlagen
            .AsNoTracking()
            .FirstOrDefaultAsync(
                entry => entry.Id == id && entry.SubjectId == subject.Value,
                cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentRecord>> GetManyAsync(
        SubjectId subject, IReadOnlyList<Guid> kennungen, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kennungen);

        if (kennungen.Count == 0)
        {
            return [];
        }

        var rows = await context.Unterlagen
            .AsNoTracking()
            .Where(row => row.SubjectId == subject.Value && kennungen.Contains(row.Id))
            .ToListAsync(cancellationToken);

        // In der REIHENFOLGE DER FRAGE, nicht in der der Datenbank: die Mappe
        // zeigt die Reiter so, wie die Person sie beigelegt hat.
        var nachKennung = rows.ToDictionary(row => row.Id);

        return
        [
            .. kennungen
                .Where(nachKennung.ContainsKey)
                .Select(id => ZumAggregat(nachKennung[id]))
        ];
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        DocumentRecord document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

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
        var vorhanden = await context.Unterlagen
            .AsTracking()
            .FirstOrDefaultAsync(row => row.Id == document.Id, cancellationToken);

        if (vorhanden is null)
        {
            vorhanden = new DocumentRecordRow { Id = document.Id };
            context.Unterlagen.Add(vorhanden);
        }

        vorhanden.SubjectId = document.Wer.Value;
        vorhanden.Name = document.Name;
        vorhanden.Kind = document.Art;
        vorhanden.ContentType = document.Inhaltstyp;
        vorhanden.SizeBytes = document.Groesse;
        vorhanden.StorageKey = document.Ablageschluessel;
        vorhanden.CreatedAt = document.Hochgeladen.UtcDateTime;
    }

    /// <inheritdoc />
    public Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default) =>
        context.Unterlagen
            .Where(row => row.Id == id && row.SubjectId == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static DocumentRecord ZumAggregat(DocumentRecordRow row) =>
        DocumentRecord.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            row.Name,
            row.Kind,
            row.ContentType,
            row.SizeBytes,
            row.StorageKey,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
}
