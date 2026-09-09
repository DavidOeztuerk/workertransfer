using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Nachrichten;
using WorkerTransfer.Notification.Application.Ports;

namespace WorkerTransfer.Notification.Application.Loeschung;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// Eine Kennung und sonst nichts, und kein Begründungsfeld: eine
/// Rechtfertigung von jemandem zu verlangen, der gehen will, ist ein Hebel
/// gegen ihn.
/// </remarks>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht Postfach und Einstellungen.</summary>
public sealed class PersonLoeschenHandler(ILoeschbestand bestand)
    : IRequestHandler<PersonLoeschenBefehl, int>
{
    /// <inheritdoc />
    public Task<int> Handle(PersonLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return bestand.LoescheAsync(request.Wer, cancellationToken);
    }
}
