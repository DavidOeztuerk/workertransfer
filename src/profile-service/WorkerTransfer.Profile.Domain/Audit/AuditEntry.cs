using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Audit;

/// <summary>A key that is not on the list.</summary>
/// <param name="key">The refused key.</param>
public sealed class AuditDataError(string key)
    : Exception($"The audit trail key '{key}' is not on the list.")
{
    /// <summary>The refused key.</summary>
    public string Key { get; } = key;
}

/// <summary>An entry in the trail. Immutable once written.</summary>
/// <remarks>
/// Free of personal data <em>by construction</em>, not by review: the
/// constructor refuses every key that is not on <see cref="AllowedKeys"/>. A
/// trail is read long after writing by people who were not there — and a
/// headline or a free text that got into it once stands in every backup.
/// </remarks>
public sealed class AuditEntry
{
    /// <summary>The only keys an entry may carry.</summary>
    /// <remarks>
    /// Technical facts about a request, never statements about a human being.
    /// What the person wrote — headline, text, location, skills — expressly
    /// does not stand on it: the content of the profile belongs in the profile
    /// table, where it is deleted, and not in a trail that outlives it.
    /// </remarks>
    public static IReadOnlySet<string> AllowedKeys { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "ip", "user_agent" };

    /// <exception cref="AuditDataError">
    /// <paramref name="data"/> carries a key that is not allowed.
    /// </exception>
    public AuditEntry(
        AuditAction action,
        DateTimeOffset occurredAt,
        SubjectId? actor = null,
        TenantId? tenant = null,
        SubjectId? target = null,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? data = null)
    {
        if (data is not null)
        {
            foreach (var key in data.Keys)
            {
                if (!AllowedKeys.Contains(key))
                {
                    throw new AuditDataError(key);
                }
            }
        }

        Action = action;
        OccurredAt = occurredAt;
        Actor = actor;
        Tenant = tenant;
        Target = target;
        CorrelationId = correlationId;
        Data = data ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>What happened.</summary>
    public AuditAction Action { get; }

    /// <summary>When.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>Who did it, as far as known.</summary>
    /// <remarks>
    /// <c>null</c> for the erasure cascade: there no human being acts, but the
    /// order one of them gave hours ago.
    /// </remarks>
    public SubjectId? Actor { get; }

    /// <summary>For which company somebody acted, or <c>null</c>.</summary>
    public TenantId? Tenant { get; }

    /// <summary>On whom, where that differs from the actor.</summary>
    public SubjectId? Target { get; }

    /// <summary>The correlation id, so a story can be read across services.</summary>
    public string? CorrelationId { get; }

    /// <summary>Technical extras, only from the list.</summary>
    public IReadOnlyDictionary<string, string> Data { get; }
}
