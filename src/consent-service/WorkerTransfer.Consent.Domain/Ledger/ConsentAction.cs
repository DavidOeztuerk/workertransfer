namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>What a fact in the ledger says happened.</summary>
/// <remarks>
/// Three, and there is no fourth. Each is a <em>new fact</em>, never an edit to
/// an existing one — which is what makes the trail readable years later without
/// the code that wrote it.
/// </remarks>
public enum ConsentAction
{
    /// <summary>Permission was given.</summary>
    Grant,

    /// <summary>Permission was taken back. Always with a reason.</summary>
    Revoke,

    /// <summary>
    /// The account was erased, and with it this capability.
    /// </summary>
    /// <remarks>
    /// Exactly one producer: the erasure cascade (ADR-0027 §1). There is no
    /// <c>POST /consent/delete</c> — it was capability-scoped, every capability
    /// here is a visibility, and equating the two would have erased a CV on a
    /// visibility withdrawal nobody asked for.
    /// </remarks>
    Delete
}
