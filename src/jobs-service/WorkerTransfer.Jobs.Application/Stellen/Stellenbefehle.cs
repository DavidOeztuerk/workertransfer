using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Jobs;

/// <summary>Was eine Anzeige ausmacht, so wie ein Aufrufer sie schickt.</summary>
public sealed record JobDetails(
    string Titel,
    string Beschreibung,
    string Ort,
    string Postleitzahl,
    Remotegrad Remote,
    EmploymentKind Art,
    IReadOnlyList<string> Faehigkeiten);

/// <summary>Schreibt eine neue Anzeige. Sie beginnt als Entwurf.</summary>
/// <remarks>
/// <c>Firma</c> kommt aus dem geprüften Token und nie aus dem Rumpf: eine
/// Mandantenkennung auf der Leitung wäre ein Weg, für ein fremdes Unternehmen
/// zu schreiben.
/// </remarks>
public sealed record CreateJobCommand(TenantId Firma, JobDetails Angaben)
    : IBefehl<Job>;

/// <inheritdoc cref="StelleAnlegenBefehl" />
public sealed class CreateJobHandler(IJobStore store, TimeProvider uhr)
    : IRequestHandler<CreateJobCommand, Job>
{
    /// <inheritdoc />
    public async Task<Job> Handle(
        CreateJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var angaben = request.Angaben;

        var job = Job.Lege_an(
            request.Firma, angaben.Titel, angaben.Beschreibung, angaben.Ort,
            angaben.Postleitzahl,
            angaben.Remote, angaben.Art, SkillList.Aus(angaben.Faehigkeiten),
            uhr.GetUtcNow());

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}

/// <summary>Ändert eine Anzeige.</summary>
public sealed record ChangeJobCommand(TenantId Firma, Guid Id, JobDetails Angaben)
    : IBefehl<Job?>;

/// <inheritdoc cref="StelleAendernBefehl" />
/// <remarks>
/// <c>null</c> heißt <em>gibt es nicht oder gehört einem anderen Unternehmen</em>,
/// und die Api muss beides gleich beantworten: ein Unterschied verriete, welche
/// Anzeigen es anderswo gibt.
/// </remarks>
public sealed class ChangeJobHandler(IJobStore store, TimeProvider uhr)
    : IRequestHandler<ChangeJobCommand, Job?>
{
    /// <inheritdoc />
    public async Task<Job?> Handle(
        ChangeJobCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await store.GetAsync(request.Id, cancellationToken);

        if (job is null || job.Firma != request.Firma)
        {
            return null;
        }

        var angaben = request.Angaben;

        job.Aendere(
            angaben.Titel, angaben.Beschreibung, angaben.Ort, angaben.Postleitzahl,
            angaben.Remote,
            angaben.Art, SkillList.Aus(angaben.Faehigkeiten), uhr.GetUtcNow());

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}

/// <summary>Veröffentlicht oder schließt eine Anzeige.</summary>
public sealed record ChangeStateCommand(TenantId Firma, Guid Id, JobState Ziel)
    : IBefehl<Job?>;

/// <inheritdoc cref="StandAendernBefehl" />
public sealed class ChangeStateHandler(IJobStore store, TimeProvider uhr)
    : IRequestHandler<ChangeStateCommand, Job?>
{
    /// <inheritdoc />
    public async Task<Job?> Handle(
        ChangeStateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await store.GetAsync(request.Id, cancellationToken);

        if (job is null || job.Firma != request.Firma)
        {
            return null;
        }

        var now = uhr.GetUtcNow();

        if (request.Ziel is JobState.Published)
        {
            job.Veroeffentliche(now);
        }
        else
        {
            job.Schliesse(now);
        }

        await store.SaveAsync(job, cancellationToken);

        return job;
    }
}
