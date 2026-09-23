using WorkerTransfer.Resume.Domain.Audit;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Appends to <c>audit_events</c>.</summary>
/// <remarks>
/// Adds to the change tracker and does not save. Saving here would give each
/// entry a transaction of its own, which is exactly the property the trail must
/// not have: the entry commits with the change it records or it records
/// nothing.
/// </remarks>
public sealed class EfAuditTrail(ResumeDbContext context) : IAuditTrail
{
    /// <inheritdoc />
    public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditEntries.Add(new AuditRow
        {
            Id = Guid.CreateVersion7(),
            ActorId = entry.Actor?.Value,
            TenantId = entry.Company?.Value,
            Action = entry.Action,
            TargetId = entry.Target?.Value,
            CorrelationId = entry.CorrelationId,
            OccurredAt = entry.OccurredAt.UtcDateTime
        });

        return Task.CompletedTask;
    }
}
