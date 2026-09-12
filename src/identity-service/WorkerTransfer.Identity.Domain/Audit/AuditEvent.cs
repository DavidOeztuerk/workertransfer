using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Audit;

/// <summary>A metadata key that is not on the allowlist.</summary>
public sealed class AuditMetadataException(string key)
    : Exception($"The audit metadata key '{key}' is not on the allowlist.")
{
    /// <summary>The key that was refused.</summary>
    public string Key { get; } = key;
}

/// <summary>One entry in the trail. Immutable once made.</summary>
/// <remarks>
/// Free of personal data by construction, not by review: the constructor
/// refuses any metadata key that is not on <see cref="AllowedMetadata"/>. A
/// trail is read long after it was written, by people who were not there, and
/// an address or a password that reached it once is in every backup.
/// </remarks>
public sealed class AuditEvent
{
    /// <summary>The only metadata keys an entry may carry.</summary>
    /// <remarks>
    /// Technical facts about a decision, never statements about a person.
    /// <c>role</c> is on the list because "admin" or "member" describes a
    /// permission; it says nothing about who holds it beyond what the tenant
    /// column already says.
    /// </remarks>
    public static IReadOnlySet<string> AllowedMetadata { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "reason", "ip", "user_agent", "role" };

    /// <exception cref="AuditMetadataException">
    /// <paramref name="metadata"/> carries a key that is not allowed.
    /// </exception>
    public AuditEvent(
        AuditAction action,
        DateTimeOffset occurredAt,
        SubjectId? actor = null,
        TenantId? tenant = null,
        SubjectId? target = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (metadata is not null)
        {
            foreach (var key in metadata.Keys)
            {
                if (!AllowedMetadata.Contains(key))
                {
                    throw new AuditMetadataException(key);
                }
            }
        }

        Action = action;
        OccurredAt = occurredAt;
        Actor = actor;
        Tenant = tenant;
        Target = target;
        CorrelationId = correlationId;
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>What happened.</summary>
    public AuditAction Action { get; }

    /// <summary>When it happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>
    /// Who did it, where that is known.
    /// </summary>
    /// <remarks>
    /// <c>null</c> on a sign-in attempt against an address nobody holds: there
    /// is no actor to name, and inventing one would be a claim.
    /// </remarks>
    public SubjectId? Actor { get; }

    /// <summary>The company acted for, or <c>null</c> for a person acting as themselves.</summary>
    public TenantId? Tenant { get; }

    /// <summary>Who it was done to, where that differs from the actor.</summary>
    public SubjectId? Target { get; }

    /// <summary>The request this belongs to, so one story can be read across services.</summary>
    public string? CorrelationId { get; }

    /// <summary>Technical detail, from the allowlist only.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
