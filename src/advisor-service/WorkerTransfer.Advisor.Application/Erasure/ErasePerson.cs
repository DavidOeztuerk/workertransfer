using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Domain.Audit;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Domain.Conversations;

namespace WorkerTransfer.Advisor.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>The mandate, the conversations and the outbox entries about them.</summary>
/// <remarks>
/// <para><strong>All three, and in one transaction.</strong> The mandate
/// belongs to the person; the conversations are about them and carry their id;
/// so do the outbox rows. Whoever deletes only one leaves the others behind —
/// and the promise of ADR-0027 is then half kept without anybody
/// noticing.</para>
///
/// <para><strong>No retention case.</strong> Unlike a hired application or a
/// paid transfer, nothing here belongs to somebody else: a conversation is a
/// relation, not a record. The answer is therefore always zero.</para>
///
/// <para><strong>The releases do not fall here.</strong> They stand in the
/// ledger, and consent-service is a recipient of the same cascade in its own
/// right — there one DELETE row is appended per capability (ADR-0027). Revoking
/// them here as well would be a second place for the same erasure.</para>
/// </remarks>
public sealed class ErasePersonHandler(
    IConversationStore store,
    IAuditTrail trail,
    TimeProvider clock)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>How much deliberately stayed behind. Always zero.</returns>
    public async Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kept = await store.DeleteAsync(request.Subject, cancellationToken);

        // In the same transaction as the erasure (the command runs in one):
        // both commit, or neither. THAT it ran, never what it deleted.
        await trail.AppendOnceAsync(
            new AuditEntry(AuditAction.Erased, request.Subject, clock.GetUtcNow()),
            cancellationToken);

        return kept;
    }
}
