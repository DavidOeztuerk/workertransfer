using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>The person answers one company's question.</summary>
public sealed record AnswerRequestCommand(Guid ResumeRequest, SubjectId Akteur, bool Erteilen)
    : IBefehl<ResumeRequestResult>;

/// <summary>Writes the ledger first and the proceeding second.</summary>
/// <remarks>
/// The order is load-bearing. The ledger call happens inside the transaction;
/// if it fails, <see cref="EinwilligungSchweigt"/> travels out and nothing is
/// committed. The other way round could leave a proceeding at
/// <see cref="Anfragestand.Granted"/> with no permission behind it.
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
    IConsentGate tor,
    IOutbox outbox,
    IAuditTrail pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<AnswerRequestCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        AnswerRequestCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.ResumeRequest, cancellationToken);

        // A foreign request id behaves like a foreign subject id: not there and
        // not mine are the same thing from outside.
        if (requestRecord is null || requestRecord.Wer != request.Akteur)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        var now = uhr.GetUtcNow();

        try
        {
            if (request.Erteilen)
            {
                requestRecord.Erteile(request.Akteur, now);
            }
            else
            {
                requestRecord.LehneAb(request.Akteur, now);
            }
        }
        catch (ResumeRequestRule rule)
        {
            return new ResumeRequestResult.Regelverstoss(rule.Code, rule.Message);
        }

        if (request.Erteilen)
        {
            await tor.GrantAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);
        }
        else
        {
            await tor.RevokeAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);
        }

        await store.SaveAsync(requestRecord, cancellationToken);

        // To whoever asked, not to the company as such: an outbox row names one
        // person. `null` means that account is gone, and then there is nobody
        // to tell.
        if (requestRecord.Frager is { } frager)
        {
            await outbox.NoteAsync(
                frager,
                request.Erteilen ? Benachrichtigungsarten.Erteilt : Benachrichtigungsarten.Abgelehnt,
                cancellationToken);
        }

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                request.Erteilen ? AuditAction.RequestGranted : AuditAction.RequestDeclined,
                now, akteur: requestRecord.Wer, company: requestRecord.Firma, betroffene: requestRecord.Wer),
            cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }
}
