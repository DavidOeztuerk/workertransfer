using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Makes an issued access token worthless in every service at once.</summary>
/// <remarks>
/// Ending a session only stops the next refresh. The access token already out
/// there is verified by signature in thirteen other services and stays good
/// until it expires — unless it is written into the shared revocation store.
/// </remarks>
public interface IAccessRevocation
{
    /// <summary>Revokes one access token — the one a sign-out was made with.</summary>
    /// <param name="tokenId">Its <c>jti</c>.</param>
    /// <param name="expiresAt">When it would have expired anyway.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RevokeTokenAsync(string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Revokes every access token of a person issued before <paramref name="cutoff"/>.</summary>
    /// <param name="subject">Whose.</param>
    /// <param name="cutoff">Tokens issued before this moment are refused.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RevokeEverythingBeforeAsync(SubjectId subject, DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
