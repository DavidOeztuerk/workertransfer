using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Domain.Audit;
using WorkerTransfer.Assessment.Application.Messages;
using WorkerTransfer.Assessment.Domain.Cases;

namespace WorkerTransfer.Assessment.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>The cases, with their tasks, submissions and evaluations.</summary>
/// <remarks>
/// <para><strong>No retention case, and expressly not for the evaluation
/// either.</strong> What stands here is no record belonging to somebody else —
/// unlike a hired application or a paid transfer (ADR-0027 §3). An evaluation
/// that survived the erasure would be the reference letter ADR-0042 rules out,
/// only at a particularly bad moment. The answer is therefore always zero.</para>
///
/// <para><strong>The releases do not fall here.</strong> They stand in the
/// ledger, and consent-service is a recipient of the same cascade in its own
/// right. Revoking them here as well would be a second place for the same
/// erasure.</para>
/// </remarks>
public sealed class ErasePersonHandler(
    ICaseStore store,
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
