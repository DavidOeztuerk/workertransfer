namespace WorkerTransfer.Consent.Domain.Audit;

/// <summary>Appends to the trail.</summary>
/// <remarks>
/// Append only — no read, no delete. The write joins whatever transaction the
/// caller has open: a decision recorded outside the transaction that made it
/// can outlive a rollback or be missing after a commit, and either way the
/// trail states something that did not happen (ADR-0012).
/// </remarks>
public interface IAuditTrail
{
    /// <param name="eintrag">What to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AnhaengenAsync(AuditEvent eintrag, CancellationToken cancellationToken = default);
}
