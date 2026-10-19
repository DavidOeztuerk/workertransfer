using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Application.Requests;

/// <summary>The person answers one company's question.</summary>
public sealed record AnswerRequestCommand(Guid RequestId, SubjectId Actor, bool Grant)
    : ICommandMessage<ResumeRequestResult>;

/// <summary>Writes the ledger first and the proceeding second.</summary>
/// <remarks>
/// The order is load-bearing. The ledger call happens inside the transaction;
/// if it fails, <see cref="ConsentGateSilent"/> travels out and nothing is
/// committed. The other way round could leave a proceeding at
/// <see cref="RequestState.Granted"/> with no permission behind it.
/// <para>
/// That leaves one gap: the ledger call succeeds and the commit then fails, so
/// the permission exists while the proceeding stays pending. Which is why a
/// <em>refusal</em> withdraws as well. A "no" then does not mean "nothing was
/// granted" but "this company holds nothing", and every answer leads the system
/// back into a safe state. The ledger takes a withdrawal without a preceding
/// grant: it appends a REVOKE event, and the answer afterwards reads as it did
/// before.
/// </para>
/// </remarks>
public sealed class AnswerRequestHandler(
    IRequestStore store,
    IConsentGate gate,
    IOutbox outbox,
    IAuditTrail auditTrail,
    IAuditEntryFactory entries,
    TimeProvider clock) : IRequestHandler<AnswerRequestCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        AnswerRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.RequestId, cancellationToken);

        // A foreign request id behaves like a foreign subject id: not there and
        // not mine are the same thing from outside.
        if (requestRecord is null || requestRecord.Subject != request.Actor)
        {
            return new ResumeRequestResult.NotVisible();
        }

        var now = clock.GetUtcNow();

        try
        {
            if (request.Grant)
            {
                requestRecord.Grant(request.Actor, now);
            }
            else
            {
                requestRecord.Decline(request.Actor, now);
            }
        }
        catch (ResumeRequestRule rule)
        {
            return new ResumeRequestResult.RuleBroken(rule.Code, rule.Message);
        }

        if (request.Grant)
        {
            await gate.GrantAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }
        else
        {
            await gate.RevokeAsync(requestRecord.Subject, requestRecord.Company, cancellationToken);
        }

        await store.SaveAsync(requestRecord, cancellationToken);

        // To whoever asked, not to the company as such: an outbox row names one
        // person. `null` means that account is gone, and then there is nobody
        // to tell.
        if (requestRecord.AskedBy is { } askedBy)
        {
            await outbox.NoteAsync(
                askedBy,
                request.Grant ? NotificationKinds.Granted : NotificationKinds.Declined,
                cancellationToken);
        }

        await auditTrail.AppendAsync(
            entries.Create(
                request.Grant ? AuditAction.RequestGranted : AuditAction.RequestDeclined,
                now, actor: requestRecord.Subject, company: requestRecord.Company,
                target: requestRecord.Subject),
            cancellationToken);

        return new ResumeRequestResult.Done(requestRecord);
    }
}
