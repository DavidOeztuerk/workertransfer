using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>What a company gets when it asks to read a résumé.</summary>
/// <remarks>
/// Two cases only, and the second one covers both "there is none" and "it is
/// not released to you". They must stay indistinguishable: a status code that
/// told them apart would be an oracle over every guessed id (ADR-0020 §1).
/// </remarks>
public abstract record ResumeResult
{
    private ResumeResult()
    {
    }

    /// <summary>Here it is.</summary>
    public sealed record Gefunden(CurriculumVitae CurriculumVitae) : ResumeResult;

    /// <summary>Hidden or non-existent — from outside the same thing.</summary>
    public sealed record NichtSichtbar : ResumeResult;
}

/// <summary>A company reads one person's résumé.</summary>
public sealed record VisibleResumeQuery(SubjectId Wer, TenantId Firma)
    : IAbfrage<ResumeResult>;

/// <inheritdoc cref="SichtbarerLebenslaufAbfrage" />
public sealed class VisibleResumeHandler(
    IResumeStore store,
    IConsentGate tor) : IRequestHandler<VisibleResumeQuery, ResumeResult>
{
    /// <inheritdoc />
    public async Task<ResumeResult> Handle(
        VisibleResumeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resume = await store.GetAsync(request.Wer, cancellationToken);

        if (resume is null)
        {
            // No ledger call for a résumé that does not exist: a needless round
            // trip, and it would report guessed subject ids to the ledger.
            return new ResumeResult.NichtSichtbar();
        }

        // ConsentGateSilent travels on deliberately: the endpoint turns it
        // into 503, which is neither "there is nothing here" nor showing it.
        return await tor.MayReadResumeAsync(request.Wer, request.Firma, cancellationToken)
            ? new ResumeResult.Gefunden(resume)
            : new ResumeResult.NichtSichtbar();
    }
}
