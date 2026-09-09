using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>
/// Mints the access token a caller presents to every service.
/// </summary>
/// <remarks>
/// Names no signature algorithm and no claim layout. Both are the
/// infrastructure's answer; the shape it has to produce is fixed in
/// <c>docs/MIGRATION-PROMPT.md</c>, "Die Tokenform entscheidet die Reihenfolge".
/// </remarks>
public interface IAccessTokenIssuer
{
    /// <param name="subject">Who is acting.</param>
    /// <param name="email">The address the subject signed in with.</param>
    /// <param name="acting">In what capacity — for a company, or for themselves.</param>
    /// <param name="session">The sign-in this token belongs to.</param>
    /// <param name="cancellationToken">Cancels the issue.</param>
    /// <returns>The encoded access token.</returns>
    Task<string> IssueAsync(
        SubjectId subject,
        string email,
        Capacity acting,
        SessionId session,
        CancellationToken cancellationToken = default);
}
