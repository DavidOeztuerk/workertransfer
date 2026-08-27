using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Application.Behaviors;

/// <summary>Runs every command inside one transaction.</summary>
/// <remarks>
/// Here rather than in each handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands, just not together.
/// Queries pass straight through — see <see cref="IAbfrage{TAntwort}"/>.
/// </remarks>
public sealed class TransaktionsBehavior<TAnfrage, TAntwort>(IUnitOfWork arbeitseinheit)
    : IPipelineBehavior<TAnfrage, TAntwort>
    where TAnfrage : notnull
{
    /// <inheritdoc />
    public Task<TAntwort> Handle(
        TAnfrage request,
        RequestHandlerDelegate<TAntwort> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        return request is IBefehl
            ? arbeitseinheit.InEinerTransaktionAsync(next.Invoke, cancellationToken)
            : next(cancellationToken);
    }
}
