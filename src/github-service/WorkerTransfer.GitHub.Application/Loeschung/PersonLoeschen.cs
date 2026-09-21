using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Nachrichten;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Application.Erasure;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht die Connection.</summary>
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
