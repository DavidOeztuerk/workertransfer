using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Messages;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>One's own applications.</summary>
public sealed record MyApplicationsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<JobApplication>>;

/// <summary>An application, if the caller may see it.</summary>
/// <remarks>
/// 404 for foreign and for missing stays indistinguishable. The company's
/// folder hangs on this one id; without it the interface would have to know the
/// job before it opens the application.
/// </remarks>
public sealed record ReadApplicationQuery(Guid Id, TenantId? Company, SubjectId? Person)
    : IQueryMessage<JobApplication?>;

/// <summary>The applications for a job of this company.</summary>
public sealed record ApplicationsForJobQuery(Guid Job, TenantId Company)
    : IQueryMessage<IReadOnlyList<JobApplication>>;

/// <summary>How many applications this company has in which state.</summary>
/// <remarks>
/// Numbers about one's <em>own</em> cases (ADR-0026). The DoD asks for
/// "analytics aggregated in a privacy-compliant way"; privacy-compliant here
/// does not mean "anonymised" but: no information comes out that the asker
/// does not have anyway. The company sees its applications one by one in the
/// list; the sum over them is a convenience, not new information.
/// <para>
/// The line does not run at the aggregation but at the
/// <strong>merging</strong>: a number that offsets applications against market
/// status, résumé or cases at other companies would be a statement about people
/// from sources that were released one by one. Nothing like that exists here,
/// and nothing like it should (ADR-0022/0026).
/// </para>
/// </remarks>
public sealed record CompanyCountsQuery(TenantId Company)
    : IQueryMessage<IReadOnlyDictionary<ApplicationState, int>>;

/// <summary>Answers the three list questions.</summary>
/// <remarks>
/// No consent call, and that is not an oversight. An application holds no
/// profile data; it names a <c>subject_id</c>. Whoever wants to see more asks
/// the responsible services, and there the ledger applies. Asking it here in
/// order to count what is visible anyway would be theatre.
/// </remarks>
public sealed class ApplicationLists(IApplicationStore store) :
    IRequestHandler<MyApplicationsQuery, IReadOnlyList<JobApplication>>,
    IRequestHandler<ReadApplicationQuery, JobApplication?>,
    IRequestHandler<ApplicationsForJobQuery, IReadOnlyList<JobApplication>>,
    IRequestHandler<CompanyCountsQuery, IReadOnlyDictionary<ApplicationState, int>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<JobApplication>> Handle(
        MyApplicationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForPersonAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<JobApplication?> Handle(
        ReadApplicationQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var application = await store.GetAsync(request.Id, cancellationToken);

        if (application is null)
        {
            return null;
        }

        if (request.Company is { } company && application.Company == company)
        {
            return application;
        }

        if (request.Person is { } person && application.Subject == person)
        {
            return application;
        }

        return null;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<JobApplication>> Handle(
        ApplicationsForJobQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The company travels into the query instead of filtering afterwards:
        // a foreign job delivers nothing instead of giving away that it exists.
        return store.ForJobAsync(request.Job, request.Company, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<ApplicationState, int>> Handle(
        CompanyCountsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.CountAsync(request.Company, cancellationToken);
    }
}
