using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Application.Loeschung;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// Eine Kennung und sonst nichts, und kein Begründungsfeld: eine
/// Rechtfertigung von jemandem zu verlangen, der gehen will, ist ein Hebel
/// gegen ihn.
/// </remarks>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht und sagt, was stehen blieb.</summary>
/// <remarks>
/// Die einzige Stelle, an der die Konstante gelesen wird. Ausgesetzt ist nicht
/// übersprungen: bleibt etwas stehen, soll der Ursprung es erfahren, statt es
/// zu vermuten (ADR-0027 §3.4).
/// </remarks>
public sealed class PersonLoeschenHandler(ILoeschbestand bestand)
    : IRequestHandler<PersonLoeschenBefehl, int>
{
    /// <inheritdoc />
    public Task<int> Handle(PersonLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return bestand.LoescheAsync(request.Wer, Aufbewahrung.BezahlteBehalten, cancellationToken);
    }
}
