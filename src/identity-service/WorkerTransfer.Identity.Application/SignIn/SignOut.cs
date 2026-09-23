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
public sealed record SignOutCommand(string? RefreshToken) : ICommandMessage<Unit>;

/// <summary>Ends a sign-in.</summary>
public sealed class SignOutHandler(
    ISessionService sessions,
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
