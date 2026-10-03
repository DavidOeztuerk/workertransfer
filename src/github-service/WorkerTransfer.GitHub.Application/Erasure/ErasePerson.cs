using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Domain.Audit;
using WorkerTransfer.GitHub.Application.Messages;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Application.Erasure;

/// <summary>The erasure order from identity-service (ADR-0027 §4).</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Erases the connection.</summary>
public sealed class ErasePersonHandler(
    IErasureLedger ledger,
    IAuditTrail trail,
    TimeProvider clock)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kept = await ledger.DeleteAsync(request.Subject, cancellationToken);

        // In the same transaction as the erasure (the command runs in one):
        // both commit, or neither. THAT it ran, never what it deleted.
        await trail.AppendOnceAsync(
            new AuditEntry(AuditAction.Erased, request.Subject, clock.GetUtcNow()),
            cancellationToken);

        return kept;
    }
}
