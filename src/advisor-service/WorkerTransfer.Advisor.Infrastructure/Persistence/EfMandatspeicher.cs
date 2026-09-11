using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Mandate;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Die Mandate in Postgres.</summary>
public sealed class EfMandatspeicher(AdvisorDbContext kontext) : IMandatspeicher
{
    /// <inheritdoc />
    public async Task<Mandat?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Mandate
            .FirstOrDefaultAsync(eintrag => eintrag.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZurDomaene(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(Mandat mandat, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mandat);

        // AsTracking, weil hier geändert wird. Ohne das käme die Zeile losgelöst
        // zurück, die Zuweisungen gingen ins Leere, und der Aufrufer bekäme
        // trotzdem sein 200 — der Fehler, den `PUT /resumes/…/as-cv` einmal
        // gekostet hat.
        var zeile = await kontext.Mandate
            .AsTracking()
            .FirstOrDefaultAsync(eintrag => eintrag.Id == mandat.Wer.Value, cancellationToken);

        if (zeile is null)
        {
            kontext.Mandate.Add(ZurZeile(mandat));
            return;
        }

        zeile.EntryMonth = mandat.Eintrittstermin;
        zeile.SalaryMin = mandat.GehaltMin;
        zeile.SalaryMax = mandat.GehaltMax;
        zeile.WorkloadPercent = mandat.PensumProzent;
        zeile.ExcludedDomains = [.. mandat.AusgeschlosseneUnternehmen];
        zeile.UpdatedAt = mandat.GeaendertAm.UtcDateTime;
    }

    private static MandatZeile ZurZeile(Mandat mandat) => new()
    {
        Id = mandat.Wer.Value,
        EntryMonth = mandat.Eintrittstermin,
        SalaryMin = mandat.GehaltMin,
        SalaryMax = mandat.GehaltMax,
        WorkloadPercent = mandat.PensumProzent,
        ExcludedDomains = [.. mandat.AusgeschlosseneUnternehmen],
        UpdatedAt = mandat.GeaendertAm.UtcDateTime
    };

    /// <summary>Baut das Aggregat aus der Zeile.</summary>
    /// <remarks>
    /// Über <see cref="Mandat.Stelle_her"/> und damit ohne erneute Prüfung: eine
    /// gespeicherte Zeile war bei ihrer Entstehung gültig, und sie beim Lesen
    /// abzulehnen hiesse, jemandem sein Mandat zu entziehen, weil sich eine
    /// Grenze geändert hat.
    /// </remarks>
    private static Mandat ZurDomaene(MandatZeile zeile) =>
        Mandat.Stelle_her(
            new SubjectId(zeile.Id),
            zeile.EntryMonth,
            zeile.SalaryMin,
            zeile.SalaryMax,
            zeile.WorkloadPercent,
            zeile.ExcludedDomains,
            new DateTimeOffset(zeile.UpdatedAt, TimeSpan.Zero));
}
