using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Application.Erasure;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// Eine Kennung und sonst nichts, und kein Begründungsfeld: eine
/// Rechtfertigung von jemandem zu verlangen, der gehen will, ist ein Hebel
/// gegen ihn.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht und sagt, was stehen blieb.</summary>
/// <remarks>
/// Die einzige Job, an der die Konstante gelesen wird. Ausgesetzt ist nicht
/// übersprungen: bleibt etwas stehen, soll der Ursprung es erfahren, statt es
/// zu vermuten (ADR-0027 §3.4).
/// </remarks>
public sealed class ErasePersonHandler(IErasureLedger bestand)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return bestand.DeleteAsync(request.Wer, Retention.BezahlteBehalten, cancellationToken);
    }
}
