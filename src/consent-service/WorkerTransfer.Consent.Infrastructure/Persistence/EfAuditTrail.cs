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
public sealed class EfAuditTrail(ConsentDbContext kontext) : IAuditTrail
{
    /// <inheritdoc />
    public Task AnhaengenAsync(AuditEvent eintrag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eintrag);

        var jetzt = eintrag.OccurredAt.UtcDateTime;

        kontext.AuditEvents.Add(new AuditEventRow
        {
            Id = Guid.CreateVersion7(),
            ActorId = eintrag.Actor?.Value,
            TenantId = eintrag.Tenant?.Value,
            Action = eintrag.Action,
            TargetId = eintrag.Target?.Value,
            CorrelationId = eintrag.CorrelationId,
            OccurredAt = jetzt,
            CreatedAt = jetzt,
            UpdatedAt = jetzt,
            Metadata = JsonSerializer.Serialize(eintrag.Metadata)
        });

        return Task.CompletedTask;
    }
}
