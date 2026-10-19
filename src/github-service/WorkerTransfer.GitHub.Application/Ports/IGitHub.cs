using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>GitHub did not answer — a system state, not an outcome.</summary>
/// <remarks>
/// Not to be mapped onto "not proved": that would mean denying somebody their
/// proof because <em>we</em> could not ask just now.
/// </remarks>
public sealed class GitHubSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>Access to GitHub — read once, do not watch.</summary>
/// <remarks>
/// There is deliberately <strong>no background reconciliation</strong> here, no
/// nightly run and no webhook. ADR-0004 forbids scraping; the letter would be
/// kept by a periodic fetch, the sense would not: a platform that keeps
/// watching a human being does something different from one that looks once at
/// their request.
/// <para>
/// Incidentally that solves the sixty requests an hour: the number of fetches
/// hangs on acts by human beings, not on a clock.
/// </para>
/// </remarks>
public interface IGitHub
{
    /// <summary>Does the one-time string stand in the description of a gist?</summary>
    /// <exception cref="GitHubSilent">GitHub did not answer.</exception>
    Task<bool> HasProofGistAsync(
        string login, string nonce, CancellationToken cancellationToken = default);

    /// <summary>This account's public repositories.</summary>
    /// <remarks>
    /// Beside the evidence, the <see cref="Snapshot" /> carries whether the
    /// languages could be fetched for every one of them — ADR-0022 §3 demands
    /// that an incomplete set gives itself away as incomplete.
    /// </remarks>
    /// <exception cref="GitHubSilent">GitHub did not answer.</exception>
    Task<Snapshot> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default);
}
