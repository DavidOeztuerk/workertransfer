using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Jobs;

/// <summary>The adverts of one's own company, in every state.</summary>
public sealed record MyJobsQuery(TenantId Company) : IQueryMessage<IReadOnlyList<Job>>;

/// <inheritdoc cref="MyJobsQuery" />
public sealed class MyJobsHandler(IJobStore store)
    : IRequestHandler<MyJobsQuery, IReadOnlyList<Job>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Job>> Handle(
        MyJobsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Company, cancellationToken);
    }
}

/// <summary>The public list.</summary>
/// <remarks>
/// Without the state as a parameter: it is filtered in the store. A
/// <c>status=draft</c> on the wire would be a way to read other companies'
/// drafts.
/// <para>
/// And without any ordering by fit. How well somebody suits a job is computed in
/// the browser and shown to the <em>person</em> (ADR-0022).
/// </para>
/// </remarks>
/// <param name="PageLength">
/// How many adverts one page holds — a length, never a count of anything
/// (ADR-0026).
/// </param>
public sealed record JobSearchQuery(
    int Page,
    int PageLength,
    IReadOnlyList<string>? Skills = null,
    string Location = "",
    RemoteMode? Remote = null,
    string SearchTerm = "",
    Guid? Company = null,
    string Employment = "",
    SearchRadius? Radius = null) : IQueryMessage<JobPage>;

/// <inheritdoc cref="JobSearchQuery" />
public sealed class JobSearchHandler(IJobStore store)
    : IRequestHandler<JobSearchQuery, JobPage>
{
    /// <inheritdoc />
    public Task<JobPage> Handle(
        JobSearchQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.SearchAsync(
            request.Page, request.PageLength, request.Skills,
            request.Location, request.Remote, request.SearchTerm, request.Company,
            request.Employment, request.Radius, cancellationToken);
    }
}

/// <summary>A single advert.</summary>
/// <param name="Company">
/// The caller's company, or <c>null</c> for a private person.
/// </param>
public sealed record JobQuery(Guid Id, TenantId? Company) : IQueryMessage<Job?>;

/// <inheritdoc cref="JobQuery" />
/// <remarks>
/// A draft is visible only to its own company. For everybody else this service
/// answers as for an advert that does not exist — otherwise it would betray that
/// a company is writing something right now.
/// </remarks>
public sealed class JobHandler(IJobStore store)
    : IRequestHandler<JobQuery, Job?>
{
    /// <inheritdoc />
    public async Task<Job?> Handle(JobQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await store.GetAsync(request.Id, cancellationToken);

        if (job is null)
        {
            return null;
        }

        var own = request.Company is { } company && job.Company == company;

        return own || job.State is JobState.Published ? job : null;
    }
}
