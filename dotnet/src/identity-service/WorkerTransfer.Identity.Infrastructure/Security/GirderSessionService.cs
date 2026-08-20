using Girder.Core.Identity;
using Girder.Infrastructure.Security.Sessions;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Answers the session port with Girder's sign-in service.</summary>
public sealed class GirderSessionService(ITokenSessionService sitzungen) : ISessionService
{
    /// <inheritdoc />
    public async Task<StartedSession> StartAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default)
    {
        var begonnen = await sitzungen.SignInAsync(subject, cancellationToken: cancellationToken);

        return new StartedSession(begonnen.Session, begonnen.RefreshToken, begonnen.ExpiresAt);
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
        var ergebnis = await sitzungen.RefreshAsync(refreshToken, cancellationToken);

        return ergebnis is { Succeeded: true, Session: { } sitzung, Subject: { } subjekt,
                             RefreshToken: { } nachfolger, ExpiresAt: { } laeuftAb }
            ? new RenewedSession(sitzung, subjekt, nachfolger, laeuftAb)
            : null;
    }

    /// <inheritdoc />
    public Task EndAsync(SessionId session, CancellationToken cancellationToken = default) =>
        sitzungen.SignOutAsync(session, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Two steps, because Girder's service ends a session by its id and the
    /// caller holds a token. Consuming the token first retires it and names the
    /// session; ending that session then closes every token in the chain.
    /// </remarks>
    public async Task EndByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var ergebnis = await sitzungen.RefreshAsync(refreshToken, cancellationToken);

        if (ergebnis.Session is { } sitzung)
        {
            await sitzungen.SignOutAsync(sitzung, cancellationToken);
        }
    }
}
