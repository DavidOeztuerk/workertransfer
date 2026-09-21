using WorkerTransfer.Resume.Domain.Pruefspur;

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

        context.Pruefspur.Add(new AuditRow
        {
            Id = Guid.CreateVersion7(),
            ActorId = entry.Akteur?.Value,
            TenantId = entry.Firma?.Value,
            Action = entry.Handlung,
            TargetId = entry.Betroffene?.Value,
            CorrelationId = entry.Korrelation,
            OccurredAt = entry.Geschehen.UtcDateTime
        });

        return Task.CompletedTask;
    }
}
