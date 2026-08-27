using MediatR;
using WorkerTransfer.Profile.Application.Nachrichten;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Application.Behaviors;

/// <summary>Führt jeden Befehl in genau einer Transaktion aus.</summary>
/// <remarks>
/// Hier statt in jedem Handler, weil der Handler, der es vergisst, derjenige
/// ist, den niemand bemerkt: was er schreibt, landet trotzdem — nur nicht
/// gemeinsam. Abfragen laufen durch, siehe <see cref="IAbfrage{TAntwort}"/>.
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
