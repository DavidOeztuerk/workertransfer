using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>Die Vorgänge in Postgres.</summary>
public sealed class EfVorgangsspeicher(AssessmentDbContext kontext) : IVorgangsspeicher
{
    /// <inheritdoc />
    public async Task<Vorgang?> HoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Vorgaenge
            .FirstOrDefaultAsync(eintrag => eintrag.Id == id, cancellationToken);

        return zeile is null ? null : ZurDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Vorgang>> FuerPersonAsync(
        SubjectId wer, CancellationToken cancellationToken = default) =>
        [.. (await kontext.Vorgaenge
                .Where(eintrag => eintrag.SubjectId == wer.Value)
                .OrderByDescending(eintrag => eintrag.UpdatedAt)
                .ThenByDescending(eintrag => eintrag.Id)
                .ToListAsync(cancellationToken))
            .Select(ZurDomaene)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Vorgang>> FuerFirmaAsync(
        TenantId firma, CancellationToken cancellationToken = default) =>
        [.. (await kontext.Vorgaenge
                .Where(eintrag => eintrag.TenantId == firma.Value)
                .OrderByDescending(eintrag => eintrag.UpdatedAt)
                .ThenByDescending(eintrag => eintrag.Id)
                .ToListAsync(cancellationToken))
            .Select(ZurDomaene)];

    /// <inheritdoc />
    public async Task SichereAsync(Vorgang vorgang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vorgang);

        // AsTracking, weil hier geändert wird. Ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200 — der Fehler, den `PUT /resumes/…/as-cv` einmal
        // gekostet hat. Der Einfügepfad verbirgt ihn, weil `Add` immer
        // verfolgt; auffallen würde er erst beim ersten Einreichen.
        var zeile = await kontext.Vorgaenge
            .AsTracking()
            .FirstOrDefaultAsync(eintrag => eintrag.Id == vorgang.Id, cancellationToken);

        if (zeile is null)
        {
            kontext.Vorgaenge.Add(ZurZeile(vorgang));
            return;
        }

        // Aufgabe, Wer und Firma stehen nicht hier: sie ändern sich nie. Eine
        // gestellte Aufgabe nachträglich umzuschreiben hiesse, den Umfang zu
        // ändern, nachdem jemand ihn gelesen und zugesagt hat.
        zeile.SubmissionText = vorgang.Einreichung?.Text;
        zeile.SubmissionUrl = vorgang.Einreichung?.Adresse;
        zeile.SubmittedAt = vorgang.Einreichung?.Am.UtcDateTime;
        zeile.EvaluationText = vorgang.Bewertung?.Text;
        zeile.EvaluationOutcome = vorgang.Bewertung is { } bewertung
            ? Woerter.Wort(bewertung.Ausgang)
            : null;
        zeile.EvaluatedAt = vorgang.Bewertung?.Am.UtcDateTime;
        zeile.UpdatedAt = vorgang.GeaendertAm.UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vorgänge <em>und</em> Postausgangszeilen. Wer nur die eine Tabelle
    /// leert, hinterlässt Vermerke mit der Kennung eines Menschen, den es nicht
    /// mehr gibt — und die Zusage aus ADR-0027 ist dann zur Hälfte eingelöst,
    /// ohne dass es jemandem auffällt.
    /// </remarks>
    public async Task<int> LoescheAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var vorgaenge = await kontext.Vorgaenge
            .AsTracking()
            .Where(eintrag => eintrag.SubjectId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Vorgaenge.RemoveRange(vorgaenge);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die Vorgänge
        // wegnimmt, liesse in jeder Sicherung stehen, dass es diese Person gab.
        var vermerke = await kontext.Set<OutboxZeile>()
            .AsTracking()
            .Where(eintrag => eintrag.UserId == wer.Value)
            .ToListAsync(cancellationToken);

        kontext.Set<OutboxZeile>().RemoveRange(vermerke);

        // Kein Aufbewahrungsfall: hier steht nichts, was jemand anderem gehoert
        // (ADR-0042, ADR-0027 §3). Eine Bewertung, die die Loeschung ueberlebte,
        // waere genau das Zeugnis, das dieser Dienst ausschliesst.
        return 0;
    }

    private static VorgangsZeile ZurZeile(Vorgang vorgang) => new()
    {
        Id = vorgang.Id,
        SubjectId = vorgang.Wer.Value,
        TenantId = vorgang.Firma.Value,
        Title = vorgang.Aufgabe.Titel,
        Task = vorgang.Aufgabe.Text,
        Hours = vorgang.Aufgabe.Stunden,
        DueAt = vorgang.Aufgabe.Frist.UtcDateTime,
        SubmissionText = vorgang.Einreichung?.Text,
        SubmissionUrl = vorgang.Einreichung?.Adresse,
        SubmittedAt = vorgang.Einreichung?.Am.UtcDateTime,
        EvaluationText = vorgang.Bewertung?.Text,
        EvaluationOutcome = vorgang.Bewertung is { } bewertung
            ? Woerter.Wort(bewertung.Ausgang)
            : null,
        EvaluatedAt = vorgang.Bewertung?.Am.UtcDateTime,
        CreatedAt = vorgang.GestelltAm.UtcDateTime,
        UpdatedAt = vorgang.GeaendertAm.UtcDateTime
    };

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über die <c>Stelle_her</c>-Fabriken und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem seinen Vorgang zu entziehen, weil sich eine
    /// Grenze geändert hat — und damit seine Bewertung, die er immer lesen darf.
    /// </remarks>
    private static Vorgang ZurDomaene(VorgangsZeile zeile) =>
        Vorgang.Stelle_her(
            zeile.Id,
            new SubjectId(zeile.SubjectId),
            new TenantId(zeile.TenantId),
            Aufgabe.Stelle_her(
                zeile.Title, zeile.Task, zeile.Hours, Zeit(zeile.DueAt)),
            zeile.SubmittedAt is { } eingereicht
                ? Einreichung.Stelle_her(
                    zeile.SubmissionText ?? string.Empty, zeile.SubmissionUrl, Zeit(eingereicht))
                : null,
            zeile.EvaluatedAt is { } bewertet
                ? Bewertung.Stelle_her(
                    zeile.EvaluationText ?? string.Empty,
                    Woerter.LiesAusgang(zeile.EvaluationOutcome) ?? Ausgang.Abgelehnt,
                    Zeit(bewertet))
                : null,
            Zeit(zeile.CreatedAt),
            Zeit(zeile.UpdatedAt));

    private static DateTimeOffset Zeit(DateTime wert) => new(wert, TimeSpan.Zero);
}
