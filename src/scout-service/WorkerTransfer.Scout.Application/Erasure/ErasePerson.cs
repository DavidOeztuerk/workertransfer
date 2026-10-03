using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Domain.Audit;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>This human being's saved searches — and the notes about them.</summary>
/// <remarks>
/// <para><strong>Both, and in one transaction.</strong> The saved searches
/// belong to the human being who saved them; the outbox rows <em>are
/// about</em> a human being and carry their identifier. Whoever erases only the
/// one leaves the other behind — and the promise of ADR-0027 is then half kept
/// without anybody noticing.</para>
///
/// <para>This service knows no retention case: nothing stands here that belongs
/// to somebody else. The answer is therefore always zero.</para>
/// </remarks>
public sealed class ErasePersonHandler(
    ISearchStore store,
    IAuditTrail trail,
    TimeProvider clock)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>How much was deliberately kept. Always zero.</returns>
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
