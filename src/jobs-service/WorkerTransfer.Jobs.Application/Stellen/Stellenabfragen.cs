using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Nachrichten;
using WorkerTransfer.Jobs.Domain.Jobs;

namespace WorkerTransfer.Jobs.Application.Jobs;

/// <summary>Die Anzeigen des eigenen Unternehmens, alle Stände.</summary>
public sealed record MyJobsQuery(TenantId Firma) : IAbfrage<IReadOnlyList<Job>>;

/// <inheritdoc cref="MeineStellenAbfrage" />
public sealed class MyJobsHandler(IJobStore store)
    : IRequestHandler<MyJobsQuery, IReadOnlyList<Job>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Job>> Handle(
        MyJobsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.ForCompanyAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Die öffentliche Liste.</summary>
/// <remarks>
/// Ohne State als Parameter: der wird im Speicher gefiltert. Ein
/// <c>status=draft</c> auf der Leitung wäre ein Weg, fremde Entwürfe zu lesen.
/// <para>
/// Und ohne jede Sortierung nach Passung. Wie gut jemand zu einer Job passt,
/// wird im Browser gerechnet und der <em>Person</em> gezeigt (ADR-0022).
/// </para>
/// </remarks>
public sealed record JobSearchQuery(
    int Seite,
    int Anzahl,
    IReadOnlyList<string>? Faehigkeiten = null,
    string Ort = "",
    Remotegrad? Remote = null,
    string Suchbegriff = "",
    Guid? Firma = null,
    string Beschaeftigung = "",
    Umkreis? Umkreis = null) : IAbfrage<JobPage>;

/// <inheritdoc cref="StellensucheAbfrage" />
public sealed class JobSearchHandler(IJobStore store)
    : IRequestHandler<JobSearchQuery, JobPage>
{
    /// <inheritdoc />
    public Task<JobPage> Handle(
        JobSearchQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.SearchAsync(
            request.Seite, request.Anzahl, request.Faehigkeiten,
            request.Ort, request.Remote, request.Suchbegriff, request.Firma,
            request.Beschaeftigung, request.Umkreis, cancellationToken);
    }
}

/// <summary>Eine einzelne Anzeige.</summary>
/// <param name="Firma">
/// Das Unternehmen des Aufrufers, oder <c>null</c> für eine Privatperson.
/// </param>
public sealed record JobQuery(Guid Id, TenantId? Firma) : IAbfrage<Job?>;

/// <inheritdoc cref="StelleAbfrage" />
/// <remarks>
/// Ein Entwurf ist nur für sein eigenes Unternehmen sichtbar. Für alle anderen
/// antwortet dieser Dienst wie bei einer Anzeige, die es nicht gibt — sonst
/// verriete er, dass ein Unternehmen gerade etwas schreibt.
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

        var eigene = request.Firma is { } company && job.Firma == company;

        return eigene || job.State is JobState.Published ? job : null;
    }
}
