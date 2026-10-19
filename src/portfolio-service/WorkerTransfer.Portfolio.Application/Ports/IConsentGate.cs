using Noelia.Core.Identity;

namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>The ledger did not answer.</summary>
/// <remarks>
/// An error of its own and not a <c>false</c>: "I do not know" is something
/// different from "no", and confusing the two means hiding a portfolio that is
/// released — or showing one that is not.
/// </remarks>
public sealed class ConsentGateSilent(string message) : Exception(message);

/// <summary>Asks the consent ledger, every single time.</summary>
/// <remarks>
/// Nothing is cached (ADR-0013). A revocation has to take effect at the next
/// read — a cache here would not be a performance detail but a broken promise.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Whether this portfolio may be shown.</summary>
    /// <remarks>
    /// <b>The same capability covers the attachments</b> (ADR-0021). A second
    /// gate for files would be a second truth, and a revocation would then
    /// take effect on the description but not on the work sample.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<bool> MaySeeAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
