using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Requests;

namespace WorkerTransfer.Resume.Application.Requests;

/// <summary>A company asks a person for their résumé.</summary>
public sealed record RequestResumeCommand(SubjectId Subject, TenantId Company, SubjectId AskedBy)
    : ICommandMessage<ResumeRequestResult>;

/// <summary>
/// Opens the proceeding — if the person's profile is released.
/// </summary>
/// <remarks>
/// The precondition is the <em>profile</em> release, never the existence of a
/// résumé. Checking both would be an oracle: "has already written a CV" is a
/// fact about the person that nobody should be able to probe for, and a company
/// that got a different answer for a person with a résumé than for one without
/// would have learned it without asking.
/// </remarks>
public sealed class RequestResumeHandler(
    IRequestStore store,
    IConsentGate gate,
    IOutbox outbox,
    IAuditTrail auditTrail,
    IAuditEntryFactory entries,
    TimeProvider clock) : IRequestHandler<RequestResumeCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RequestResumeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await gate.MaySeeProfileAsync(request.Subject, cancellationToken))
        {
            return new ResumeRequestResult.NotVisible();
        }

        if (await store.FindAsync(request.Subject, request.Company, cancellationToken) is not null)
        {
            return new ResumeRequestResult.AlreadyAsked();
        }

        var now = clock.GetUtcNow();
        var requestRecord = ResumeRequest.Open(
            request.Subject, request.Company, request.AskedBy, now);

        await store.AppendAsync(requestRecord, cancellationToken);

        // In the SAME transaction as the request (ADR-0025). If the request
        // gets through, the notification is settled; if it rolls back, nobody
        // is told a company asked when none did.
        await outbox.NoteAsync(request.Subject, NotificationKinds.Requested, cancellationToken);

        await auditTrail.AppendAsync(
            entries.Create(
                AuditAction.ResumeRequested, now,
                actor: request.AskedBy, company: request.Company, target: request.Subject),
            cancellationToken);

        return new ResumeRequestResult.Done(requestRecord);
    }
}
