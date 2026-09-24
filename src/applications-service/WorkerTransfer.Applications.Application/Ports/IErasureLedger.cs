using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>What "erase" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// The switch travels in as a parameter instead of being read inside. That is
/// the difference between "the switch stands at off" and "the flipped switch
/// covers exactly one row class" — the second statement is the one somebody
/// must be able to check without changing anything for it.
/// </remarks>
public interface IErasureLedger
{
    /// <summary>Erases this person's applications.</summary>
    /// <param name="subject">Whose.</param>
    /// <param name="keepHired">
    /// Whether <c>hired</c> rows stay. By default <c>false</c>
    /// (<see cref="Erasure.Retention"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many deliberately stayed. By default: 0.</returns>
    Task<int> DeleteAsync(
        SubjectId subject,
        bool keepHired,
        CancellationToken cancellationToken = default);
}
