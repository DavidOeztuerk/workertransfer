using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Application.Loeschung;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// Eine Kennung und sonst nichts. Das ist keine Sparsamkeit — ein Löschbefehl
/// <em>hat</em> keinen Inhalt, und kein Begründungsfeld, weil eine
/// Rechtfertigung von jemandem zu verlangen, der gehen will, ein Hebel gegen
/// ihn ist.
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

        return bestand.LoescheAsync(
            request.Wer, Aufbewahrung.EingestellteBehalten, cancellationToken);
    }
}
