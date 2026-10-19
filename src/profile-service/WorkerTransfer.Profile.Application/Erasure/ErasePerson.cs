using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Messages;
using WorkerTransfer.Profile.Domain.Audit;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Application.Erasure;

/// <summary>"Delete everything you hold about this human being."</summary>
/// <param name="Subject">The person.</param>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>What "erase" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// The row falls completely: headline, text, location, skills. There is nothing
/// here that belongs to somebody else, so nothing to keep either and nothing to
/// anonymise.
/// <para>
/// No visibility field one could flip instead: whether a profile may be shown
/// lives in the ledger alone (ADR-0020). "Switching it invisible" would
/// therefore not be the gentler erasure here, but none at all.
/// </para>
/// <para>
/// Answers with the number of rows that remained — always 0. The value is
/// there anyway, because every recipient gives the same receipt: the origin is
/// to learn what remained instead of guessing it.
/// </para>
/// </remarks>
public sealed class ErasePersonHandler(
    IProfileStore store,
    IAuditTrail trail,
    TimeProvider clock) : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    public async Task<int> Handle(
        ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kept = await store.DeleteAsync(request.Subject, cancellationToken);

        // The trail records THAT something was deleted and names no actor: no
        // human being acts here, but the order one of them gave hours ago. What
        // was deleted stands nowhere — that would be a copy of what just
        // disappeared.
        await trail.AppendOneAsync(
            new AuditEntry(
                AuditAction.ProfileDeleted, clock.GetUtcNow(), target: request.Subject),
            cancellationToken);

        return kept;
    }
}
