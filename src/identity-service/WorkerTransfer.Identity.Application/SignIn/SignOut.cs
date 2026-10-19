using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Sessions;

namespace WorkerTransfer.Identity.Application.SignIn;

/// <summary>End the sign-in the presented refresh token belongs to.</summary>
/// <param name="RefreshToken">
/// What the caller presented, if anything. Nothing is a supported input:
/// someone asking to be signed out is entitled to be signed out, and telling
/// them their token was already dead helps nobody and says something.
/// </param>
public sealed record SignOutCommand(
    string? RefreshToken,
    string? AccessTokenId = null,
    DateTimeOffset? AccessExpiresAt = null) : ICommandMessage<Unit>;

/// <summary>Ends a sign-in.</summary>
public sealed class SignOutHandler(
    ISessionService sessions,
    IAccessRevocation revocation,
    ISessionCapacity sessionCapacity,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<SignOutCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(SignOutCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The access token first, and even without a refresh token: ending
        // the session only stops the next refresh, while the access token
        // would stay good in every other service until it expires.
        if (request is { AccessTokenId: { Length: > 0 } tokenId, AccessExpiresAt: { } expiresAt })
        {
            await revocation.RevokeTokenAsync(tokenId, expiresAt, cancellationToken);
        }

        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Unit.Value;
        }

        var ended = await sessions.EndByRefreshTokenAsync(
            request.RefreshToken, cancellationToken);

        if (ended is null)
        {
            return Unit.Value;
        }

        // The sign-in is over, so what it was acting as is no longer an answer
        // to anything. Leaving the row would keep a company on a session that
        // no longer exists.
        await sessionCapacity.ForgetAsync(ended.Session, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.TokenRevoke,
                clock.GetUtcNow(),
                actor: ended.Subject,
                correlationId: correlation.Current),
            cancellationToken);

        return Unit.Value;
    }
}
