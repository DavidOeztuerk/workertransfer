using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Application.Behaviors;

/// <summary>Führt jeden Befehl in einer Transaktion aus.</summary>
/// <remarks>
/// Hier statt in jedem Handler, weil der Handler, der es vergisst, derjenige
/// ist, den niemand bemerkt: alles, was er schreibt, landet trotzdem — nur
/// nicht gemeinsam.
/// <para>
/// In diesem Dienst hängt daran mehr als eine Zeile: der Vermerk „dein Profil
/// wurde entdeckt" entsteht im Postausgang und muss mit dem Befehl gemeinsam
/// festgeschrieben werden oder gar nicht (ADR-0025).
/// </para>
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
