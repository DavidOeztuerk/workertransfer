using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Persistence;

/// <summary>Die Vorgänge in Postgres.</summary>
public sealed class EfCaseStore(AssessmentDbContext context) : ICaseStore
{
    /// <inheritdoc />
    public async Task<Case?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Vorgaenge
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return row is null ? null : ZurDomaene(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Case>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        [.. (await context.Vorgaenge
                .Where(entry => entry.SubjectId == subject.Value)
                .OrderByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ZurDomaene)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Case>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        [.. (await context.Vorgaenge
                .Where(entry => entry.TenantId == company.Value)
                .OrderByDescending(entry => entry.UpdatedAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ZurDomaene)];

    /// <inheritdoc />
    public async Task SaveAsync(Case caseItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        // AsTracking, weil hier geändert wird. Ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200 — der Fehler, den `PUT /resumes/…/as-cv` einmal
        // gekostet hat. Der Einfügepfad verbirgt ihn, weil `Add` immer
        // verfolgt; auffallen würde er erst beim ersten Einreichen.
        var row = await context.Vorgaenge
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == caseItem.Id, cancellationToken);

        if (row is null)
        {
            context.Vorgaenge.Add(ZurZeile(caseItem));
            return;
        }

        // Aufgabe, Wer und Firma stehen nicht hier: sie ändern sich nie. Eine
        // gestellte Aufgabe nachträglich umzuschreiben hiesse, den Umfang zu
        // ändern, nachdem jemand ihn gelesen und zugesagt hat.
        row.SubmissionText = caseItem.Submission?.Text;
        row.SubmissionUrl = caseItem.Submission?.Adresse;
        row.SubmittedAt = caseItem.Submission?.Am.UtcDateTime;
        row.EvaluationText = caseItem.Evaluation?.Text;
        row.EvaluationOutcome = caseItem.Evaluation is { } evaluation
            ? Woerter.Wort(evaluation.Ausgang)
            : null;
        row.EvaluatedAt = caseItem.Evaluation?.Am.UtcDateTime;
        row.UpdatedAt = caseItem.GeaendertAm.UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Vorgänge <em>und</em> Postausgangszeilen. Wer nur die eine Tabelle
    /// leert, hinterlässt Vermerke mit der Kennung eines Menschen, den es nicht
    /// mehr gibt — und die Zusage aus ADR-0027 ist dann zur Hälfte eingelöst,
    /// ohne dass es jemandem auffällt.
    /// </remarks>
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var cases = await context.Vorgaenge
            .AsTracking()
            .Where(entry => entry.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Vorgaenge.RemoveRange(cases);

        // Und die Vermerke ÜBER diesen Menschen. Sie tragen seine Kennung und
        // sind damit personenbezogen — eine Löschung, die nur die Vorgänge
        // wegnimmt, liesse in jeder Sicherung stehen, dass es diese Person gab.
        var vermerke = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(entry => entry.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(vermerke);

        // Kein Aufbewahrungsfall: hier steht nichts, was jemand anderem gehoert
        // (ADR-0042, ADR-0027 §3). Eine Evaluation, die die Erasure ueberlebte,
        // waere genau das Reference, das dieser Dienst ausschliesst.
        return 0;
    }

    private static CaseRow ZurZeile(Case caseItem) => new()
    {
        Id = caseItem.Id,
        SubjectId = caseItem.Wer.Value,
        TenantId = caseItem.Firma.Value,
        Title = caseItem.Aufgabe.Titel,
        Task = caseItem.Aufgabe.Text,
        Hours = caseItem.Aufgabe.Stunden,
        DueAt = caseItem.Aufgabe.Frist.UtcDateTime,
        SubmissionText = caseItem.Submission?.Text,
        SubmissionUrl = caseItem.Submission?.Adresse,
        SubmittedAt = caseItem.Submission?.Am.UtcDateTime,
        EvaluationText = caseItem.Evaluation?.Text,
        EvaluationOutcome = caseItem.Evaluation is { } evaluation
            ? Woerter.Wort(evaluation.Ausgang)
            : null,
        EvaluatedAt = caseItem.Evaluation?.Am.UtcDateTime,
        CreatedAt = caseItem.GestelltAm.UtcDateTime,
        UpdatedAt = caseItem.GeaendertAm.UtcDateTime
    };

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über die <c>Stelle_her</c>-Fabriken und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem seinen Case zu entziehen, weil sich eine
    /// Grenze geändert hat — und damit seine Evaluation, die er immer lesen darf.
    /// </remarks>
    private static Case ZurDomaene(CaseRow row) =>
        Case.Stelle_her(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            Aufgabe.Stelle_her(
                row.Title, row.Task, row.Hours, Zeit(row.DueAt)),
            row.SubmittedAt is { } eingereicht
                ? Submission.Stelle_her(
                    row.SubmissionText ?? string.Empty, row.SubmissionUrl, Zeit(eingereicht))
                : null,
            row.EvaluatedAt is { } bewertet
                ? Evaluation.Stelle_her(
                    row.EvaluationText ?? string.Empty,
                    Woerter.LiesAusgang(row.EvaluationOutcome) ?? Ausgang.Abgelehnt,
                    Zeit(bewertet))
                : null,
            Zeit(row.CreatedAt),
            Zeit(row.UpdatedAt));

    private static DateTimeOffset Zeit(DateTime value) => new(value, TimeSpan.Zero);
}
