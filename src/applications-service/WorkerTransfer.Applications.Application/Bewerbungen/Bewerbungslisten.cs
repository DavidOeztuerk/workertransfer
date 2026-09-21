using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Nachrichten;
using WorkerTransfer.Applications.Domain.Applications;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Die eigenen Applications.</summary>
public sealed record MyApplicationsQuery(SubjectId Wer)
    : IAbfrage<IReadOnlyList<JobApplication>>;

/// <summary>Eine JobApplication, wenn der Aufrufer sie sehen darf.</summary>
/// <remarks>
/// 404 für fremd und für nicht vorhanden bleibt ununterscheidbar. Die Mappe
/// des Unternehmens hängt an dieser einen Kennung; ohne sie müsste die
/// Oberfläche die Job kennen, bevor sie die JobApplication öffnet.
/// </remarks>
public sealed record ReadApplicationQuery(Guid Id, TenantId? Firma, SubjectId? Person)
    : IAbfrage<JobApplication?>;

/// <summary>Die Applications auf eine Job dieses Unternehmens.</summary>
public sealed record ApplicationsForJobQuery(Guid Job, TenantId Firma)
    : IAbfrage<IReadOnlyList<JobApplication>>;

/// <summary>Wie viele Applications dieses Unternehmen in welchem State hat.</summary>
/// <remarks>
/// Zahlen über die <em>eigenen</em> Vorgänge (ADR-0026). Die DoD verlangt
/// „Analytics aggregiert datenschutzkonform"; datenschutzkonform heißt hier
/// nicht „anonymisiert", sondern: es kommt keine Auskunft heraus, die der
/// Fragende nicht ohnehin hat. Das Unternehmen sieht seine Applications einzeln
/// in der Liste; die Summe darüber ist eine Bequemlichkeit, keine neue
/// Information.
/// <para>
/// Die Grenze verläuft nicht bei der Aggregation, sondern bei der
/// <strong>Zusammenführung</strong>: eine Zahl, die Applications mit
/// MarketStatus, CurriculumVitae oder Vorgängen bei anderen Firmen verrechnet, wäre
/// eine Aussage über Menschen aus Quellen, die einzeln freigegeben wurden. So
/// etwas gibt es hier nicht und soll es nicht geben (ADR-0022/0026).
/// </para>
/// </remarks>
public sealed record CompanyCountsQuery(TenantId Firma)
    : IAbfrage<IReadOnlyDictionary<ApplicationState, int>>;

/// <summary>Beantwortet die drei Listenfragen.</summary>
/// <remarks>
/// Kein Consent-Aufruf, und das ist kein Versehen. Eine JobApplication enthält keine
/// Profildaten; sie nennt eine <c>subject_id</c>. Wer mehr sehen will, fragt
/// die zuständigen Dienste, und dort greift der Ledger. Ihn hier zu fragen,
/// um zu zählen, was ohnehin sichtbar ist, wäre Theater.
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

        return store.ForPersonAsync(request.Wer, cancellationToken);
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

        if (request.Firma is { } company && application.Firma == company)
        {
            return application;
        }

        if (request.Person is { } person && application.Wer == person)
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

        // Die Firma reist mit in die Abfrage, statt hinterher zu filtern:
        // eine fremde Job liefert nichts, statt zu verraten, dass es sie
        // gibt.
        return store.ForJobAsync(request.Job, request.Firma, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<ApplicationState, int>> Handle(
        CompanyCountsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return store.CountAsync(request.Firma, cancellationToken);
    }
}
