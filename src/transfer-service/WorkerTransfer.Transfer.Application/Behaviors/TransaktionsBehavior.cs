using MediatR;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Application.Behaviors;

/// <summary>Führt jeden Befehl in einer Transaktion aus.</summary>
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
