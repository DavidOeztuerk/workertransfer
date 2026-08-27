namespace WorkerTransfer.Identity.Domain.Audit;

/// <summary>Appends to the trail.</summary>
/// <remarks>
/// Append only — there is no read and no delete here. Reading it is an
/// operator's task and not this service's; deleting from it would defeat what
/// it is for. Account erasure clears the <em>contents</em> of the affected
/// columns rather than dropping rows (ADR-0027 §5), which is a different
/// operation and belongs with the erasure.
/// <para>
/// The write joins whatever transaction the caller has open. That is the whole
/// point: a decision recorded outside the transaction that made it can outlive
/// a rollback, or be missing after a commit, and either way the trail says
/// something that did not happen.
/// </para>
/// </remarks>
public interface IAuditTrail
{
    /// <param name="entry">What to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendAsync(AuditEvent entry, CancellationToken cancellationToken = default);
}
