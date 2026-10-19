using System.Text.Json;
using WorkerTransfer.Profile.Domain.Audit;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Appends to <c>audit_events</c>.</summary>
/// <remarks>
/// Puts into change tracking and does <em>not</em> save. Saving here would give
/// every entry a transaction of its own — exactly the property the trail must
/// not have: the entry commits with the change it records, or it records
/// nothing.
/// </remarks>
public sealed class EfAuditTrail(ProfileDbContext context) : IAuditTrail
{
    /// <inheritdoc />
    public Task AppendOneAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.AuditEntries.Add(new AuditRow
        {
            // Time-ordered: the trail is read by time, and a random id would
            // scatter the inserts over the whole index.
            Id = Guid.CreateVersion7(),
            ActorId = entry.Actor?.Value,
            TenantId = entry.Tenant?.Value,
            Action = entry.Action,
            TargetId = entry.Target?.Value,
            CorrelationId = entry.CorrelationId,
            OccurredAt = entry.OccurredAt,
            Metadata = JsonSerializer.Serialize(entry.Data)
        });

        return Task.CompletedTask;
    }
}
