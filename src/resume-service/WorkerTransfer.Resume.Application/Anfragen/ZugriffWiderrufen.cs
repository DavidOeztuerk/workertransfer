using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>The person takes a release back.</summary>
public sealed record RevokeAccessCommand(Guid ResumeRequest, SubjectId Akteur)
    : IBefehl<ResumeRequestResult>;

/// <summary>Works in the ledger, and leaves the proceeding untouched.</summary>
/// <remarks>
/// <see cref="Anfragestand.Granted"/> means "was granted once". Changing it on a
/// withdrawal would rewrite history — and whether access holds is answered by
/// the ledger anyway. This is the single place where somebody reading the code
/// is most tempted to "fix" a bug that is the design.
/// </remarks>
public sealed class RevokeAccessHandler(
    IRequestStore store,
    IConsentGate tor,
    IAuditTrail pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<RevokeAccessCommand, ResumeRequestResult>
{
    /// <inheritdoc />
    public async Task<ResumeRequestResult> Handle(
        RevokeAccessCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestRecord = await store.GetAsync(request.ResumeRequest, cancellationToken);

        if (requestRecord is null || requestRecord.Wer != request.Akteur)
        {
            return new ResumeRequestResult.NichtSichtbar();
        }

        await tor.RevokeAsync(requestRecord.Wer, requestRecord.Firma, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(
                AuditAction.AccessRevoked, uhr.GetUtcNow(),
                akteur: requestRecord.Wer, company: requestRecord.Firma, betroffene: requestRecord.Wer),
            cancellationToken);

        return new ResumeRequestResult.Erledigt(requestRecord);
    }
}
