namespace WorkerTransfer.Applications.Domain.Audit;

/// <summary>Appends to the trail — in the same transaction as the change it
/// records (ADR-0012), never as a log line.</summary>
public interface IAuditTrail
{
    /// <summary>Appends the entry unless the same action already stands for the same person.</summary>
    /// <remarks>
    /// The erasure is delivered at least once (ADR-0025). A second delivery must
    /// not record a second erasure: the person deleted once, not twice.
    /// </remarks>
    /// <param name="entry">What to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendOnceAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
