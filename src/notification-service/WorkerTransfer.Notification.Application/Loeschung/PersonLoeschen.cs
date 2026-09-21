using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Nachrichten;
using WorkerTransfer.Notification.Application.Ports;

namespace WorkerTransfer.Notification.Application.Erasure;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// Eine Kennung und sonst nichts, und kein Begründungsfeld: eine
/// Rechtfertigung von jemandem zu verlangen, der gehen will, ist ein Hebel
/// gegen ihn.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht Inbox und Einstellungen.</summary>
public sealed class ErasePersonHandler(IErasureLedger bestand)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return bestand.DeleteAsync(request.Wer, cancellationToken);
    }
}
