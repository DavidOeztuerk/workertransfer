using MediatR;
using WorkerTransfer.Portfolio.Application.Nachrichten;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Application.Behaviors;

/// <summary>Führt jeden Befehl in einer Transaktion aus.</summary>
/// <remarks>
/// Hier statt in jedem Handler, weil der Handler, der es vergisst, derjenige
/// ist, den niemand bemerkt: alles, was er schreibt, landet trotzdem — nur
/// nicht gemeinsam.
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
