using Noelia.Core.Identity;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>The ledger did not answer. No result — a system state.</summary>
public sealed class ConsentGateSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>The release key, built in exactly one place.</summary>
public static class ConsentKey
{
    /// <summary>
    /// A portfolio of evidenced work is a thing one shows.
    /// </summary>
    /// <remarks>
    /// Hence <c>:public</c> and not recipient-scoped: unlike the market status,
    /// "this human being has public repositories" says nothing that gets them
    /// into trouble with their employer — it stands on github.com anyway. The
    /// personal datum is not the name but the <em>link</em>: "this platform
    /// person is that GitHub name", and exactly that is what this key releases.
    /// </remarks>
    public const string Visibility = "github.visibility:public";
}

/// <summary>Asks the ledger. Never writes into it.</summary>
/// <remarks>
/// Synchronously and without a cache (ADR-0013): a revocation has to take
/// effect at the very next read.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Whether this person's connection may be shown.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MayBeShownAsync(
        SubjectId subject, CancellationToken cancellationToken = default);
}
