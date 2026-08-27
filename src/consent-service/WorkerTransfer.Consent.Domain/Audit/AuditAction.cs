namespace WorkerTransfer.Consent.Domain.Audit;

/// <summary>What the trail records, as a closed set.</summary>
/// <remarks>
/// Structurally the same idea as identity-service's, and deliberately not
/// shared with it: audit payloads are service-owned (ADR-0012) and there is no
/// cross-service domain model (ADR-0004 §1). The actions differ — this service
/// records consent changes, not sign-ins.
/// </remarks>
public enum AuditAction
{
    /// <summary>Permission was given.</summary>
    ConsentGrant,

    /// <summary>Permission was taken back.</summary>
    ConsentRevoke,

    /// <summary>An account erasure closed a capability.</summary>
    ConsentDelete
}
