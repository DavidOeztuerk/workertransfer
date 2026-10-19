using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Messages;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Application.Erasure;

/// <summary>The erasure order from identity-service (ADR-0027 §4).</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Erases the connection.</summary>
public sealed class ErasePersonHandler(IErasureLedger ledger)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ledger.DeleteAsync(request.Subject, cancellationToken);
    }
}
