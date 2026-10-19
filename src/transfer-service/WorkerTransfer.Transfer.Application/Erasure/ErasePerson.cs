using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Messages;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Application.Erasure;

/// <summary>The erasure order from identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// One id and nothing else, and no reason field: demanding a justification from
/// somebody who wants to leave is a lever against them.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Erases and says what stayed.</summary>
/// <remarks>
/// The one place where the constant is read. Suspended is not skipped: if
/// something stays, the origin should learn of it instead of guessing
/// (ADR-0027 §3.4).
/// </remarks>
public sealed class ErasePersonHandler(IErasureLedger ledger)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ledger.DeleteAsync(request.Subject, Retention.KeepPaid, cancellationToken);
    }
}
