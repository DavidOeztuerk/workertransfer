using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Resumes;

namespace WorkerTransfer.Resume.Application.Resumes;

/// <summary>Write or rewrite one's own résumé.</summary>
/// <remarks>
/// Owner only. There is no command that writes somebody else's résumé, and the
/// subject is not in the body — it comes from the verified token, so a caller
/// cannot name a different one.
/// </remarks>
public sealed record SaveResumeCommand(
    SubjectId Subject,
    IReadOnlyList<Position> Positions,
    IReadOnlyList<Education> Education) : ICommandMessage<CurriculumVitae>;

/// <summary>Creates it the first time and replaces it afterwards.</summary>
public sealed class SaveResumeHandler(
    IResumeStore store,
    IAuditTrail auditTrail,
    IAuditEntryFactory entries,
    TimeProvider clock) : IRequestHandler<SaveResumeCommand, CurriculumVitae>
{
    /// <inheritdoc />
    public async Task<CurriculumVitae> Handle(
        SaveResumeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var existing = await store.GetAsync(request.Subject, cancellationToken);

        CurriculumVitae resume;

        if (existing is null)
        {
            resume = CurriculumVitae.Create(
                request.Subject, request.Positions, request.Education, now);
        }
        else
        {
            existing.Update(request.Positions, request.Education, now);
            resume = existing;
        }

        await store.SaveAsync(resume, cancellationToken);

        await auditTrail.AppendAsync(
            entries.Create(AuditAction.ResumeSaved, now, request.Subject, target: request.Subject),
            cancellationToken);

        return resume;
    }
}
