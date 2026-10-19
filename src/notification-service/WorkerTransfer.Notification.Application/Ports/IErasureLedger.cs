using Noelia.Core.Identity;

namespace WorkerTransfer.Notification.Application.Ports;

/// <summary>What "erase" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// Two statements, both without exception: the inbox and the preferences fall.
/// <strong>No retention switch</strong> — it was never claimed that a row here
/// is subject to a retention obligation, and one "just in case" is exactly the
/// precautionary assumption ADR-0027 §3 abolishes.
/// </remarks>
public interface IErasureLedger
{
    /// <summary>Erases everything about this human being.</summary>
    /// <returns>What stayed. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
