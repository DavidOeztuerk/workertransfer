using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Nachrichten;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Application.Loeschung;

/// <summary>Der Löschbefehl von identity-service (ADR-0027 §4).</summary>
public sealed record PersonLoeschenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Löscht die Verbindung.</summary>
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
