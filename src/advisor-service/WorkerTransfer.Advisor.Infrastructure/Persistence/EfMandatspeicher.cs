using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Die Mandate in Postgres.</summary>
public sealed class EfMandateStore(AdvisorDbContext context) : IMandateStore
{
    /// <inheritdoc />
    public async Task<Mandate?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Mandate
            .FirstOrDefaultAsync(entry => entry.Id == subject.Value, cancellationToken);

        return row is null ? null : ZurDomaene(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Mandate mandate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        // AsTracking, weil hier geändert wird. Ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200 — der Fehler, den `PUT /resumes/…/as-cv` einmal
        // gekostet hat.
        var row = await context.Mandate
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == mandate.Wer.Value, cancellationToken);

        if (row is null)
        {
            context.Mandate.Add(ZurZeile(mandate));
            return;
        }

        row.EntryMonth = mandate.Eintrittstermin;
        row.SalaryMin = mandate.GehaltMin;
        row.SalaryMax = mandate.GehaltMax;
        row.WorkloadPercent = mandate.PensumProzent;
        row.ExcludedDomains = [.. mandate.AusgeschlosseneUnternehmen];
        row.UpdatedAt = mandate.GeaendertAm.UtcDateTime;
    }

    private static MandateRow ZurZeile(Mandate mandate) => new()
    {
        Id = mandate.Wer.Value,
        EntryMonth = mandate.Eintrittstermin,
        SalaryMin = mandate.GehaltMin,
        SalaryMax = mandate.GehaltMax,
        WorkloadPercent = mandate.PensumProzent,
        ExcludedDomains = [.. mandate.AusgeschlosseneUnternehmen],
        UpdatedAt = mandate.GeaendertAm.UtcDateTime
    };

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Mandat.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem sein Mandate zu entziehen, weil sich eine
    /// Grenze geändert hat.
    /// </remarks>
    private static Mandate ZurDomaene(MandateRow row) =>
        Mandate.Stelle_her(
            new SubjectId(row.Id),
            row.EntryMonth,
            row.SalaryMin,
            row.SalaryMax,
            row.WorkloadPercent,
            row.ExcludedDomains,
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
}
