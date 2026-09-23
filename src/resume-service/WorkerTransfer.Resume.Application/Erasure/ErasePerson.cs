using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;

namespace WorkerTransfer.Resume.Application.Erasure;

/// <summary>The erasure order from identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// One identifier and nothing else. That is not thrift — an erasure order
/// <em>has</em> no content, and no reason field, because demanding a
/// justification from somebody who wants to leave is a lever against them.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Deletes, records that it deleted, and answers what was left.</summary>
/// <remarks>
/// The trail entry names <em>nobody</em> — no actor, no subject, no company.
/// It records that an erasure ran here and when, and it is joined to the
/// cascade by the correlation id alone. Naming the person would put back
/// exactly the row the erasure exists to remove, and the deletion below would
/// then have to sweep up its own proof (ADR-0027 §5: the argument is not "we
/// may retain" but that nothing maps a subject id to a person afterwards).
/// </remarks>
public sealed class ErasePersonHandler(
    IErasureLedger ledger,
    IAuditTrail auditTrail,
    IAuditEntryFactory entries,
    TimeProvider clock) : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(
        ErasePersonCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await auditTrail.AppendAsync(
            entries.Create(AuditAction.SubjectErased, clock.GetUtcNow()),
            cancellationToken);

        return await ledger.DeleteAsync(request.Subject, cancellationToken);
    }
}
