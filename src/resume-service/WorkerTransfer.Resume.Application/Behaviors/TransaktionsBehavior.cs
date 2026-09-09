using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Application.Behaviors;

/// <summary>Runs every command inside one transaction.</summary>
/// <remarks>
/// Here rather than in each handler, because the handler that forgets it is the
/// one nobody notices: everything it writes still lands, just not together —
/// and the outbox row that lands without its request is a notification about
/// something that never happened. Queries pass straight through.
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
