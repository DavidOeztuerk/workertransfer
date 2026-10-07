using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Messages;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Jobs;

/// <summary>What makes up an advert, as a caller sends it.</summary>
public sealed record JobDetails(
    string Title,
    string Description,
    string Location,
    string PostalCode,
    RemoteMode Remote,
    EmploymentKind Employment,
    IReadOnlyList<string> Skills,
    int? SalaryMinimum = null,
    int? SalaryMaximum = null,
    SalaryPeriod SalaryPeriod = SalaryPeriod.Year);

/// <summary>Writes a new advert. It begins as a draft.</summary>
/// <remarks>
/// <c>Company</c> comes from the verified token and never from the body: a
/// tenant id on the wire would be a way to write for a foreign company.
/// </remarks>
public sealed record CreateJobCommand(TenantId Company, JobDetails Details)
    : ICommandMessage<Job>;

/// <inheritdoc cref="CreateJobCommand" />
public sealed class CreateJobHandler(IJobStore store, TimeProvider clock)
    : IRequestHandler<CreateJobCommand, Job>
{
    /// <inheritdoc />
    public async Task<Job> Handle(
        CreateJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = request.Details;

        var job = Job.Create(
            request.Company, details.Title, details.Description, details.Location,
            details.PostalCode,
            details.Remote, details.Employment, SkillList.From(details.Skills),
            clock.GetUtcNow(),
            SalaryRange.From(details.SalaryMinimum, details.SalaryMaximum, details.SalaryPeriod));

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}

/// <summary>Changes an advert.</summary>
public sealed record ChangeJobCommand(TenantId Company, Guid Id, JobDetails Details)
    : ICommandMessage<Job?>;

/// <inheritdoc cref="ChangeJobCommand" />
/// <remarks>
/// <c>null</c> means <em>does not exist or belongs to another company</em>, and
/// the API has to answer both the same: a difference would betray which adverts
/// exist elsewhere.
/// </remarks>
public sealed class ChangeJobHandler(IJobStore store, TimeProvider clock)
    : IRequestHandler<ChangeJobCommand, Job?>
{
    /// <inheritdoc />
    public async Task<Job?> Handle(
        ChangeJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await store.GetAsync(request.Id, cancellationToken);

        if (job is null || job.Company != request.Company)
        {
            return null;
        }

        var details = request.Details;

        job.Change(
            details.Title, details.Description, details.Location, details.PostalCode,
            details.Remote,
            details.Employment, SkillList.From(details.Skills), clock.GetUtcNow(),
            SalaryRange.From(details.SalaryMinimum, details.SalaryMaximum, details.SalaryPeriod));

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}

/// <summary>Publishes or closes an advert.</summary>
public sealed record ChangeStateCommand(TenantId Company, Guid Id, JobState Target)
    : ICommandMessage<Job?>;

/// <inheritdoc cref="ChangeStateCommand" />
public sealed class ChangeStateHandler(IJobStore store, TimeProvider clock)
    : IRequestHandler<ChangeStateCommand, Job?>
{
    /// <inheritdoc />
    public async Task<Job?> Handle(
        ChangeStateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await store.GetAsync(request.Id, cancellationToken);

        if (job is null || job.Company != request.Company)
        {
            return null;
        }

        var now = clock.GetUtcNow();

        if (request.Target is JobState.Published)
        {
            job.Publish(now);
        }
        else
        {
            job.Close(now);
        }

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}
