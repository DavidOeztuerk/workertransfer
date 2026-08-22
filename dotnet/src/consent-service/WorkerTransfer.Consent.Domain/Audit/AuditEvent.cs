using Girder.Core.Identity;

namespace WorkerTransfer.Consent.Domain.Audit;

/// <summary>A metadata key that is not on the allowlist.</summary>
public sealed class AuditMetadataException(string key)
    : Exception($"The audit metadata key '{key}' is not on the allowlist.")
{
    /// <summary>The key that was refused.</summary>
    public string Key { get; } = key;
}

/// <summary>One entry in the trail. Immutable once made.</summary>
/// <remarks>
/// Free of personal data by construction rather than by review. A trail is read
/// long after it was written, by people who were not there, and anything that
/// reached it once is in every backup.
/// </remarks>
public sealed class AuditEvent
{
    /// <summary>The only metadata keys an entry may carry.</summary>
    /// <remarks>
    /// The capability <em>name</em> is a technical fact about which permission
    /// changed and is allowed; the data that permission governs never is. An
    /// audit row must not become a second copy of the payload.
    /// </remarks>
    public static IReadOnlySet<string> AllowedMetadata { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "reason", "ip", "user_agent", "capability" };

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

    /// <summary>Who did it.</summary>
    public SubjectId? Actor { get; }

    /// <summary>
    /// The company acted for, or none for a person acting as themselves.
    /// </summary>
    /// <remarks>
    /// Attribution only. A consent belongs to the person and follows them
    /// across employers (ADR-0017), which is why <c>consent_events</c> has no
    /// tenant column at all — this one is on the trail, where it says who was
    /// acting, not whose consent it is.
    /// </remarks>
    public TenantId? Tenant { get; }

    /// <summary>Whose consent it was.</summary>
    public SubjectId? Target { get; }

    /// <summary>The request this belongs to, so one story reads across services.</summary>
    public string? CorrelationId { get; }

    /// <summary>Technical detail, from the allowlist only.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }
}
