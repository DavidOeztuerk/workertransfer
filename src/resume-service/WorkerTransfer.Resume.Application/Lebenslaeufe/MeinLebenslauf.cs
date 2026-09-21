using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Nachrichten;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;

namespace WorkerTransfer.Resume.Application.Lebenslaeufe;

/// <summary>One's own résumé, with no ledger in the way.</summary>
/// <remarks>
/// Answers <c>null</c> rather than a refusal when there is none: "has not
/// written one yet" is a state, and the interface shows an empty form for it.
/// A failure here would force it to translate an error back into a normal caseItem.
/// </remarks>
public sealed record MyResumeQuery(SubjectId Wer) : IAbfrage<CurriculumVitae?>;

/// <inheritdoc cref="MeinLebenslaufAbfrage" />
public sealed class MyResumeHandler(IResumeStore store)
    : IRequestHandler<MyResumeQuery, CurriculumVitae?>
{
    /// <inheritdoc />
    public Task<CurriculumVitae?> Handle(
        MyResumeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Wer, cancellationToken);
    }
}
