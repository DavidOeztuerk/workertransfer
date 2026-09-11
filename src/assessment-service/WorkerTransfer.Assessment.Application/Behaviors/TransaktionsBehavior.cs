using MediatR;
using WorkerTransfer.Assessment.Application.Nachrichten;
using WorkerTransfer.Assessment.Application.Ports;

namespace WorkerTransfer.Assessment.Application.Behaviors;

/// <summary>Führt jeden Befehl in einer Transaktion aus.</summary>
/// <remarks>
/// Hier statt in jedem Handler, weil der Handler, der es vergisst, derjenige
/// ist, den niemand bemerkt: alles, was er schreibt, landet trotzdem — nur
/// nicht gemeinsam.
/// <para>
/// In diesem Dienst hängt daran mehr als eine Zeile: die Bewertung und der
/// Vermerk im Postausgang, der die Person davon in Kenntnis setzt, müssen
/// gemeinsam festgeschrieben werden oder gar nicht (ADR-0025). Eine Bewertung
/// ohne den Vermerk wäre eine Beurteilung, von der die Person nichts erfährt —
/// und genau das wird hier nicht gebaut.
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
