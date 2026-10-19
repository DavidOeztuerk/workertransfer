using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>The mandates in Postgres.</summary>
public sealed class EfMandateStore(AdvisorDbContext context) : IMandateStore
{
    /// <inheritdoc />
    public async Task<Mandate?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Mandates
            .FirstOrDefaultAsync(entry => entry.Id == subject.Value, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(Mandate mandate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        // AsTracking, because this changes things. Without it the row would come
        // back detached, the assignments would go nowhere, and the caller would
        // still get its 200 — the defect `PUT /resumes/…/as-cv` once cost.
        var row = await context.Mandates
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == mandate.Subject.Value, cancellationToken);

        if (row is null)
        {
            context.Mandates.Add(ToRow(mandate));
            return;
        }

        row.EntryMonth = mandate.EntryMonth;
        row.SalaryMin = mandate.SalaryMin;
        row.SalaryMax = mandate.SalaryMax;
        row.WorkloadPercent = mandate.WorkloadPercent;
        row.ExcludedDomains = [.. mandate.ExcludedDomains];
        row.UpdatedAt = mandate.UpdatedAt.UtcDateTime;
    }

    private static MandateRow ToRow(Mandate mandate) => new()
    {
        Id = mandate.Subject.Value,
        EntryMonth = mandate.EntryMonth,
        SalaryMin = mandate.SalaryMin,
        SalaryMax = mandate.SalaryMax,
        WorkloadPercent = mandate.WorkloadPercent,
        ExcludedDomains = [.. mandate.ExcludedDomains],
        UpdatedAt = mandate.UpdatedAt.UtcDateTime
    };

    /// <summary>Builds the aggregate from the row.</summary>
    /// <remarks>
    /// Through <see cref="Mandate.Restore"/> and therefore without checking
    /// again: a stored row was valid when it came about, and refusing it on read
    /// would take somebody's mandate away because a limit changed.
    /// </remarks>
    private static Mandate ToDomain(MandateRow row) =>
        Mandate.Restore(
            new SubjectId(row.Id),
            row.EntryMonth,
            row.SalaryMin,
            row.SalaryMax,
            row.WorkloadPercent,
            row.ExcludedDomains,
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
}
