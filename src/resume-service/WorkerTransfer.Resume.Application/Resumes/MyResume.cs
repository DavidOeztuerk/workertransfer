using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Resume.Application.Messages;
using WorkerTransfer.Resume.Domain.Resumes;

namespace WorkerTransfer.Resume.Application.Resumes;

/// <summary>One's own résumé, with no ledger in the way.</summary>
/// <remarks>
/// Answers <c>null</c> rather than a refusal when there is none: "has not
/// written one yet" is a state, and the interface shows an empty form for it.
/// A failure here would force it to translate an error back into a normal case.
/// </remarks>
public sealed record MyResumeQuery(SubjectId Subject) : IQueryMessage<CurriculumVitae?>;

/// <inheritdoc cref="MyResumeQuery" />
public sealed class MyResumeHandler(IResumeStore store)
    : IRequestHandler<MyResumeQuery, CurriculumVitae?>
{
    /// <inheritdoc />
    public Task<CurriculumVitae?> Handle(
        MyResumeQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.GetAsync(request.Subject, cancellationToken);
    }
}
