using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>What "erase" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// Five statements, and two run in different directions:
/// <list type="bullet">
/// <item><c>market_status</c> falls, note and all.</item>
/// <item>A request <em>about</em> the person falls — the row <em>is</em> a
/// statement about them.</item>
/// <item>A request <em>by</em> the person stays, without their name: it belongs
/// to the company and is about a third party.</item>
/// <item>Their cases fall, message and offer text and all.</item>
/// <item>Open notes in the outbox fall.</item>
/// </list>
/// <para>
/// The switch travels in as a parameter instead of being read inside.
/// Otherwise one could only check that it stands at off — not what the flipped
/// switch covers, and that is exactly the statement that matters.
/// </para>
/// </remarks>
public interface IErasureLedger
{
    /// <summary>Erases everything about this human being.</summary>
    /// <param name="subject">Whose.</param>
    /// <param name="keepPaid">
    /// Whether completed cases with a fee stay. <c>false</c> by default
    /// (<see cref="Erasure.Retention"/>).
    /// </param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How many deliberately stayed. By default: 0.</returns>
    Task<int> DeleteAsync(
        SubjectId subject,
        bool keepPaid,
        CancellationToken cancellationToken = default);
}
