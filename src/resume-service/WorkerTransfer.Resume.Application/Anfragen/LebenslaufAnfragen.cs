using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>A company asks a person for their résumé.</summary>
public sealed record RequestResumeCommand(SubjectId Wer, TenantId Firma, SubjectId Frager)
    : IBefehl<ResumeRequestResult>;

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
    IConsentGate tor,
    IOutbox outbox,
    IAuditTrail pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<RequestResumeCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RequestResumeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await tor.MaySeeProfileAsync(request.Wer, cancellationToken))
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        if (await store.FindeAsync(request.Wer, request.Firma, cancellationToken) is not null)
        {
            return new ResumeRequestResult.SchonGefragt();
        }

        var now = uhr.GetUtcNow();
        var requestRecord = ResumeRequest.Oeffne(request.Wer, request.Firma, request.Frager, now);

        await store.AppendAsync(requestRecord, cancellationToken);

        // In the SAME transaction as the request (ADR-0025). If the request
        // gets through, the notification is settled; if it rolls back, nobody
        // is told a company asked when none did.
        await outbox.NoteAsync(request.Wer, Benachrichtigungsarten.Angefragt, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                AuditAction.ResumeRequested, now,
                akteur: request.Frager, company: request.Firma, betroffene: request.Wer),
            cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }
}
