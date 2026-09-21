using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Erasure;

/// <summary>The deletion order from identity-service (ADR-0027 §4).</summary>
/// <remarks>
/// One identifier and nothing else. That is not thrift — a deletion order
/// <em>has</em> no content, and no reason field, because demanding a
/// justification from somebody who wants to leave is a lever against them.
/// </remarks>
public sealed record ErasePersonCommand(SubjectId Wer) : IBefehl<int>;

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
    IErasureLedger bestand,
    IAuditTrail pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(
        ErasePersonCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await pruefspur.AppendAsync(
            anhang.Eintrag(AuditAction.SubjectErased, uhr.GetUtcNow()),
            cancellationToken);

        return await bestand.DeleteAsync(request.Wer, cancellationToken);
    }
}
