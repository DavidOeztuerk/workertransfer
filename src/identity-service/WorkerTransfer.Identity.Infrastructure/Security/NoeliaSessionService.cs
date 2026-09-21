using Noelia.Core.Identity;
using Noelia.Abstractions.Security.Sessions;
using Noelia.Infrastructure.Security.Sessions;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Answers the session port with Noelia's sign-in service.</summary>
public sealed class NoeliaSessionService(ITokenSessionService sessions) : ISessionService
{
    /// <inheritdoc />
    public async Task<StartedSession> StartAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var started = await sessions.SignInAsync(subject, cancellationToken: cancellationToken);

        return new StartedSession(started.Session, started.RefreshToken, started.ExpiresAt);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A rotation inside the reuse grace period counts as success. Two browser
    /// tabs both hitting a 401 and both refreshing is the honest case that
    /// looks exactly like a replay, and treating it as theft signs out people
    /// who did nothing wrong.
    /// </remarks>
    public async Task<RenewedSession?> RenewAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var result = await sessions.RefreshAsync(refreshToken, cancellationToken);

        return result is { Succeeded: true, Session: { } session, Subject: { } subject,
                             RefreshToken: { } successor, ExpiresAt: { } expiresAt }
            ? new RenewedSession(session, subject, successor, expiresAt)
            : null;
    }

    /// <inheritdoc />
    public Task EndAsync(SessionId session, CancellationToken cancellationToken = default) =>
        sessions.SignOutAsync(session, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Two steps, because Noelia's service ends a session by its id and the
    /// caller holds a token. Consuming the token first retires it and names the
    /// session; ending that session then closes every token in the chain.
    /// </remarks>
    public async Task<EndedSession?> EndByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var result = await sessions.RefreshAsync(refreshToken, cancellationToken);

        if (result is not { Session: { } session, Subject: { } subject })
        {
            return null;
        }

        await sessions.SignOutAsync(session, cancellationToken);

        return new EndedSession(session, subject);
    }
}
