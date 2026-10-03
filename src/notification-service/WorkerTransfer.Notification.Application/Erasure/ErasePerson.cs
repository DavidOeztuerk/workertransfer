using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Domain.Audit;
using WorkerTransfer.Notification.Application.Messages;
using WorkerTransfer.Notification.Application.Ports;

namespace WorkerTransfer.Notification.Application.Erasure;

/// <summary>The erasure order from identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// One id and nothing else, and no reason field: demanding a justification from
/// somebody who wants to leave is a lever against them.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Erases inbox and settings.</summary>
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
