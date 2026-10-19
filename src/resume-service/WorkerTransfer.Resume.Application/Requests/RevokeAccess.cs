using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Application.Requests;

/// <summary>The person takes a release back.</summary>
public sealed record RevokeAccessCommand(Guid RequestId, SubjectId Actor)
    : ICommandMessage<ResumeRequestResult>;

/// <summary>Works in the ledger, and leaves the proceeding untouched.</summary>
/// <remarks>
/// <see cref="RequestState.Granted"/> means "was granted once". Changing it on a
/// withdrawal would rewrite history — and whether access holds is answered by
/// the ledger anyway. This is the single place where somebody reading the code
/// is most tempted to "fix" a bug that is the design.
/// </remarks>
public sealed class RevokeAccessHandler(
    IRequestStore store,
    IConsentGate gate,
    IAuditTrail auditTrail,
    IAuditEntryFactory entries,
    TimeProvider clock) : IRequestHandler<RevokeAccessCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RevokeAccessCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.RequestId, cancellationToken);

        if (requestRecord is null || requestRecord.Subject != request.Actor)
        {
            return new ResumeRequestResult.NotVisible();
        }

        await gate.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);

        await auditTrail.AppendAsync(
            entries.Create(
                AuditAction.AccessRevoked, clock.GetUtcNow(),
                actor: requestRecord.Subject, company: requestRecord.Company,
                target: requestRecord.Subject),
            cancellationToken);

        return new ResumeRequestResult.Done(requestRecord);
    }
}
