using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>Write or rewrite one's own résumé.</summary>
/// <remarks>
/// Owner only. There is no command that writes somebody else's résumé, and the
/// subject is not in the body — it comes from the verified token, so a caller
/// cannot name a different one.
/// </remarks>
public sealed record SaveResumeCommand(
    SubjectId Wer,
    IReadOnlyList<Station> Stationen,
    IReadOnlyList<Ausbildung> Ausbildungen) : IBefehl<CurriculumVitae>;

/// <summary>Creates it the first time and replaces it afterwards.</summary>
public sealed class SaveResumeHandler(
    IResumeStore store,
    IAuditTrail pruefspur,
    IKorrelationsanhang anhang,
    TimeProvider uhr) : IRequestHandler<SaveResumeCommand, CurriculumVitae>
{
    /// <inheritdoc />
    public async Task<CurriculumVitae> Handle(
        SaveResumeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();
        var vorhanden = await store.GetAsync(request.Wer, cancellationToken);

        CurriculumVitae resume;

        if (vorhanden is null)
        {
            resume = CurriculumVitae.Anlegen(
                request.Wer, request.Stationen, request.Ausbildungen, now);
        }
        else
        {
            vorhanden.Aktualisiere(request.Stationen, request.Ausbildungen, now);
            resume = vorhanden;
        }

        await store.SaveAsync(resume, cancellationToken);

        await pruefspur.AppendAsync(
            anhang.Eintrag(AuditAction.ResumeSaved, now, request.Wer, betroffene: request.Wer),
            cancellationToken);

        return resume;
    }
}
