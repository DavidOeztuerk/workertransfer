namespace WorkerTransfer.Profile.Domain.Audit;

/// <summary>Appends to the trail.</summary>
/// <remarks>
/// Only appending — no reading, no deleting. Reading is an operator's task and
/// not this service's; deleting would be the opposite of what the trail is
/// there for.
/// <para>
/// The write joins the transaction the caller has open. That is the whole
/// purpose: an entry that comes into being outside the transaction that caused
/// it survives a rollback or is missing after a commit — and in both cases says
/// something that did not happen.
/// </para>
/// </remarks>
public interface IAuditTrail
{
    /// <param name="entry">What is to be recorded.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendOneAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
