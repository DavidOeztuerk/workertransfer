using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Sessions;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>End the sign-in the presented refresh token belongs to.</summary>
/// <param name="Erneuerungstoken">
/// What the caller presented, if anything. Nothing is a supported input:
/// someone asking to be signed out is entitled to be signed out, and telling
/// them their token was already dead helps nobody and says something.
/// </param>
public sealed record AbmeldenBefehl(string? Erneuerungstoken) : IBefehl<Unit>;

/// <summary>Ends a sign-in.</summary>
public sealed class AbmeldenHandler(
    ISessionService sitzungen,
    ISessionCapacity handlungsform,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<AbmeldenBefehl, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(AbmeldenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(request.Erneuerungstoken))
        {
            return Unit.Value;
        }

        var beendet = await sitzungen.EndByRefreshTokenAsync(
            request.Erneuerungstoken, cancellationToken);

        if (beendet is null)
        {
            return Unit.Value;
        }

        // The sign-in is over, so what it was acting as is no longer an answer
        // to anything. Leaving the row would keep a company on a session that
        // no longer exists.
        await handlungsform.ForgetAsync(beendet.Session, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.TokenRevoke,
                uhr.GetUtcNow(),
                actor: beendet.Subject,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        return Unit.Value;
    }
}
