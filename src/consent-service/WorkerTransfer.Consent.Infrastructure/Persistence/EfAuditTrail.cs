using System.Text.Json;
using WorkerTransfer.Consent.Domain.Audit;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>Appends to <c>audit_events</c>.</summary>
/// <remarks>
/// Adds to the change tracker and does not save. Saving here would give each
/// entry a transaction of its own, which is exactly the property the trail must
/// not have: the entry commits with the change it records, or it records
/// nothing.
/// </remarks>
public sealed class EfAuditTrail(ConsentDbContext context) : IAuditTrail
{
    /// <inheritdoc />
    public Task AttachAsync(AuditEvent entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var now = entry.OccurredAt.UtcDateTime;

        context.AuditEvents.Add(new AuditEventRow
        {
            Id = Guid.CreateVersion7(),
            ActorId = entry.Actor?.Value,
            TenantId = entry.Tenant?.Value,
            Action = entry.Action,
            TargetId = entry.Target?.Value,
            CorrelationId = entry.CorrelationId,
            OccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            Metadata = JsonSerializer.Serialize(entry.Metadata)
        });

        return Task.CompletedTask;
    }
}
