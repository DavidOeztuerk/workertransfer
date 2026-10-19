namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>The proof over GitHub's own sign-in.</summary>
/// <remarks>
/// <strong>It replaces the gist, not the naming.</strong> The person still says
/// first which account is theirs; OAuth then answers the question whether they
/// control it. The other way round would work too — GitHub does tell us the
/// name — but then the flow would differ between the two paths, and the gist
/// path stays as long as no credentials are stored.
///
/// <para>
/// <strong>The access token is not kept.</strong> It is needed for exactly one
/// call — <c>GET /user</c>, to learn the login name. After that it is worthless
/// to us and would only be somebody else's secret in our database. What you do
/// not keep you cannot lose.
/// </para>
///
/// <para>
/// Hence <strong>no scope</strong> either: <c>GET /user</c> answers the
/// question without any <c>scope</c>. Asking for a <c>repo</c> right in order
/// to read public repositories would be a power of attorney for something that
/// is public anyway.
/// </para>
/// </remarks>
public interface IGitHubSignIn
{
    /// <summary>Is a sign-in over GitHub configured at all?</summary>
    /// <remarks>
    /// Without credentials the gist stays the path. The interface asks this to
    /// know which path it offers — and not in order to show a button that then
    /// does not work.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Where the browser is sent.</summary>
    /// <param name="state">
    /// The connection's one-time string. GitHub hands it back unchanged; it
    /// proves the answer belongs to THIS request and not to one somebody else
    /// slipped in.
    /// </param>
    Uri AuthorizeUrl(string state);

    /// <summary>Exchanges the one-time code for the login name.</summary>
    /// <remarks>
    /// Returns <c>null</c> when GitHub does not accept the code — that is a
    /// statement about the code, not an outage.
    /// </remarks>
    /// <exception cref="GitHubSilent">GitHub did not answer.</exception>
    Task<string?> LoginNameAsync(string code, CancellationToken cancellationToken = default);
}
