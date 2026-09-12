using Girder.Core.Identity;

namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>A metadata key that is not on the allowlist.</summary>
public sealed class ConsentMetadataException(string key)
    : Exception($"The consent metadata key '{key}' is not on the allowlist.")
{
    /// <summary>The key that was refused.</summary>
    public string Key { get; } = key;
}

/// <summary>A withdrawal without a reason.</summary>
public sealed class ReasonRequiredException()
    : Exception("Withdrawing a capability must always be explainable.");

/// <summary>
/// One immutable fact about one (subject, capability) pair.
/// </summary>
/// <remarks>
/// There is no mutable consent aggregate and no status column. Grant, revoke
/// and delete are facts appended to a log; the current state is
/// <em>computed</em> from them (<see cref="Projektion"/>), so there is exactly
/// one place consent can be true and no second store to fall out of sync.
/// <para>
/// The constructor is private and the three factories are the only entrances,
/// because two of the rules — a revocation carries a reason, an erasure does
/// not demand one — are properties of <em>which</em> fact this is.
/// </para>
/// </remarks>
public sealed class ConsentEvent
{
    /// <summary>The only metadata keys a fact may carry.</summary>
    /// <remarks>
    /// A consent event describes <em>that</em> a permission changed, never the
    /// personal data the permission is about. The list is copied from the audit
    /// side rather than shared with it: audit payloads are service-owned
    /// (ADR-0012), and one shared list would be the coupling ADR-0004 forbids.
    /// </remarks>
    public static IReadOnlySet<string> AllowedMetadata { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "reason", "ip", "user_agent", "actor_id" };

    private ConsentEvent(
        Guid eventId,
        SubjectId subject,
        Capability capability,
        ConsentAction action,
        DateTimeOffset recordedAt,
        SubjectId? actor,
        WithdrawalReason? reason,
        IReadOnlyDictionary<string, string>? metadata,
        bool ausDerAblage)
    {
        if (metadata is not null)
        {
            foreach (var key in metadata.Keys)
            {
                if (!AllowedMetadata.Contains(key))
                {
                    throw new ConsentMetadataException(key);
                }
            }
        }

        // Only on the way in. Checked rather than left to the signature of
        // <see cref="Revoke"/>, because the guarantee is worth keeping when
        // somebody adds a fourth entrance.
        //
        // Not on the way back out: account erasure clears the free text of
        // every row it finds, a withdrawal included (ADR-0027 §5). Refusing to
        // read such a row afterwards would make the erasure the thing that
        // breaks the ledger — and the ledger is what proves the erasure
        // happened. The Python service checked both ways and would have thrown
        // on its own erased rows.
        if (!ausDerAblage && action is ConsentAction.Revoke && reason is null)
        {
            throw new ReasonRequiredException();
        }

        EventId = eventId;
        Subject = subject;
        Capability = capability;
        Action = action;
        RecordedAt = recordedAt;
        Actor = actor;
        Reason = reason;
        Metadata = metadata ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>The identity of this fact, and the idempotency key of its row.</summary>
    public Guid EventId { get; }

    /// <summary>Whose permission this is.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which permission.</summary>
    public Capability Capability { get; }

    /// <summary>What happened.</summary>
    public ConsentAction Action { get; }

    /// <summary>When, to the tick that orders the log.</summary>
    public DateTimeOffset RecordedAt { get; }

    /// <summary>Who did it. Absent for a fact restored from a row that has none.</summary>
    public SubjectId? Actor { get; }

    /// <summary>Why it was withdrawn. Mandatory for a withdrawal, absent otherwise.</summary>
    public WithdrawalReason? Reason { get; }

    /// <summary>Technical detail, from the allowlist only.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Permission was given. No justification needed.</summary>
    public static ConsentEvent Grant(
        SubjectId subject,
        Capability capability,
        DateTimeOffset recordedAt,
        SubjectId? actor = null,
        WithdrawalReason? reason = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(Guid.CreateVersion7(), subject, capability, ConsentAction.Grant, recordedAt, actor,
            reason, metadata, ausDerAblage: false);

    /// <summary>Permission was taken back, and said why.</summary>
    public static ConsentEvent Revoke(
        SubjectId subject,
        Capability capability,
        DateTimeOffset recordedAt,
        WithdrawalReason reason,
        SubjectId? actor = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(Guid.CreateVersion7(), subject, capability, ConsentAction.Revoke, recordedAt, actor,
            reason, metadata, ausDerAblage: false);

    /// <summary>
    /// The account was erased. Deliberately without a reason.
    /// </summary>
    /// <remarks>
    /// Demanding a justification from somebody who wants to leave is a lever
    /// against them — and the free text would be the one thing §5 has to remove
    /// again straight afterwards.
    /// </remarks>
    public static ConsentEvent Delete(
        SubjectId subject,
        Capability capability,
        DateTimeOffset recordedAt,
        SubjectId? actor = null) =>
        new(Guid.CreateVersion7(), subject, capability, ConsentAction.Delete, recordedAt, actor,
            null, null, ausDerAblage: false);

    /// <summary>Rebuilds a fact from a stored row.</summary>
    /// <remarks>
    /// The one entrance that takes an existing identity and an existing time,
    /// and the one that does not demand a reason on a withdrawal — see the
    /// constructor.
    /// </remarks>
    public static ConsentEvent Restore(
        Guid eventId,
        SubjectId subject,
        Capability capability,
        ConsentAction action,
        DateTimeOffset recordedAt,
        SubjectId? actor,
        WithdrawalReason? reason,
        IReadOnlyDictionary<string, string>? metadata) =>
        new(eventId, subject, capability, action, recordedAt, actor, reason, metadata,
            ausDerAblage: true);
}
