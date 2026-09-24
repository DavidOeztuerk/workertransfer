using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Application.Erasure;

/// <summary>The erasure command from identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// An id and nothing else. That is not thrift — an erasure command <em>has</em>
/// no content, and no reason field, because demanding a justification from
/// someone who wants to leave is a lever against them.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Erases and says what stayed.</summary>
/// <remarks>
/// The only place where the constant is read. Suspended is not skipped: if
/// something stays, the origin should learn it rather than guess
/// (ADR-0027 §3.4).
/// </remarks>
public sealed class ErasePersonHandler(IErasureLedger ledger)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ledger.DeleteAsync(
            request.Subject, Retention.KeepHired, cancellationToken);
    }
}
