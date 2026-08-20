using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>A sign-in that just began.</summary>
/// <param name="Session">Names the sign-in for its whole life, across every rotation.</param>
/// <param name="RefreshToken">The only place this value exists; the store holds its hash.</param>
/// <param name="ExpiresAt">When this token stops working.</param>
public sealed record StartedSession(SessionId Session, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>A sign-in that was carried forward.</summary>
/// <param name="Session">The same sign-in as before — rotation does not start a new one.</param>
/// <param name="Subject">
/// Whose sign-in it is, from the stored record rather than from what the caller sent.
/// </param>
/// <param name="RefreshToken">The successor. The presented one no longer counts.</param>
/// <param name="ExpiresAt">When the successor stops working.</param>
public sealed record RenewedSession(
    SessionId Session,
    SubjectId Subject,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

/// <summary>Beginning, carrying forward and ending a sign-in.</summary>
/// <remarks>
/// Names no storage and no token format. The refresh token is opaque by
/// contract: nothing reads anything out of it, so nothing here says what is in
/// it.
/// </remarks>
public interface ISessionService
{
    /// <summary>Begins a sign-in and issues its first refresh token.</summary>
    Task<StartedSession> StartAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exchanges a refresh token for its successor.
    /// </summary>
    /// <returns>
    /// The renewed sign-in, or <c>null</c> when the presented token does not
    /// entitle the caller to one — expired, already used, or never issued. The
    /// three are not told apart: which one it was is not the caller's business.
    /// </returns>
    Task<RenewedSession?> RenewAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Ends one sign-in. Idempotent.</summary>
    Task EndAsync(SessionId session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends the sign-in a refresh token belongs to, and retires the token.
    /// </summary>
    /// <remarks>
    /// Idempotent: a token that was never issued, already used or expired has
    /// no sign-in to end, and that is not an error.
    /// </remarks>
    Task EndByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
