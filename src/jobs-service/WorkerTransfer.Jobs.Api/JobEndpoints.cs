using System.Globalization;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Api.Authorization;
using WorkerTransfer.Jobs.Application.Draft;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Jobs;
using WorkerTransfer.Jobs.Contracts;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>/jobs</c> and <c>/companies/me/jobs</c>.</summary>
public static class JobEndpoints
{
    /// <summary>Maps the job routes.</summary>
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var jobs = app.MapGroup("/jobs");

        jobs.MapPost("/draft", async (
            DraftV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            if (actor.Acting is not Capacity.ForCompany)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            try
            {
                // The tenant_id is DELIBERATELY not passed on: the draft context
                // has no field for it, so nothing can slip in either (ADR-0024).
                var text = await mediator.Send(
                    new DraftAdvertQuery(
                        body.Title, body.Description, body.Skills ?? [],
                        body.Location, body.Wish),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (DraftUnavailable error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", error.Message);
            }
            catch (InputError error)
            {
                // A wish that is too long is input, not an outage: 422, not 503.
                // Without this branch it would leave the service as a 500 — and
                // a 500 tells the caller it is not their fault.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        jobs.MapPost("/", async (
            WriteJobV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!ActingCompany(principal, out var company))
            {
                await NoCompany(context, principal);
                return;
            }

            await Answer(context, cancellationToken, async () =>
            {
                var job = await mediator.Send(
                    new CreateJobCommand(company, Details(body)), cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(ToContract(job), cancellationToken);
            });
        });

        jobs.MapPut("/{id:guid}", async (
            Guid id,
            WriteJobV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!ActingCompany(principal, out var company))
            {
                await NoCompany(context, principal);
                return;
            }

            await Answer(context, cancellationToken, async () =>
            {
                var job = await mediator.Send(
                    new ChangeJobCommand(company, id, Details(body)), cancellationToken);

                await Show(context, job, cancellationToken);
            });
        });

        // The two state changes are the only places here where the advert
        // represents the company to the outside — and therefore the only ones
        // that demand an `admin`. Writing and changing stay open to every
        // member: a draft faces nobody.
        //
        // The policy is resolved by `AdminPermission` from identity-service's
        // membership table, not from the token — whoever is removed is out at
        // the next request.
        jobs.MapPost("/{id:guid}/publish", (
            Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
            Transition(id, JobState.Published, mediator, principal, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Policy(JobPermissions.Publish));

        jobs.MapPost("/{id:guid}/close", (
            Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
            Transition(id, JobState.Closed, mediator, principal, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Policy(JobPermissions.Close));

        // Public, WITHOUT sign-in, and that is a correction — and without any
        // ordering by fit. How well somebody suits a job is computed by the
        // browser and shown to the person (ADR-0022).
        //
        // A lock on `principal.Current is null` stood here. It revealed nothing
        // that would not be public anyway — the store filters on
        // `status == "published"`, and published means "visible to everybody,
        // even without an account" in this domain. But it cost exactly what an
        // advert is there for: `/careers/<slug>` is documented as a public page
        // (docs/routenkarte.yml) and still showed an anonymous visitor not a
        // single job. An advert that can only be seen after signing in misses
        // exactly the people it is meant for.
        //
        // Drafts and closed adverts stay inside: the state is filtered in the
        // store and never by the caller.
        jobs.MapGet("/", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var query = context.Request.Query;

            // `q`, `company` and `employment` had always been sent by the
            // interface and were NEVER read here. So the search form searched
            // nothing, the employment filter filtered nothing — and the careers
            // page showed the adverts of ALL companies under the name of a
            // single one.
            var location = query["location"].ToString();

            // The radius comes as three numbers and becomes a value or nothing
            // HERE. `lat` and `lon` are already rounded in the browser (two
            // decimal places, a good kilometre) — they stand in an address bar
            // and thus in access logs, and more precision could change no
            // answer, because the adverts' places are town centres.
            var radiusKm = Number(query["radius_km"]);

            var radius = SearchRadius.From(Number(query["lat"]), Number(query["lon"]), radiusKm)
                // WITHOUT coordinates the LOCATION is the centre. Whoever types
                // "Leipzig" and chooses "50 km" means "around Leipzig" — and
                // needs neither GPS nor the permission for it.
                ?? (PlaceLookup.Find(null, location) is { } centre
                    ? SearchRadius.From(centre.Latitude, centre.Longitude, radiusKm)
                    : null);

            var page = await mediator.Send(
                new JobSearchQuery(
                    PageSelection.Page(query["page"]),
                    PageSelection.Size(query["page_size"]),
                    query["skill"].Count > 0 ? [.. query["skill"]!] : null,
                    // WITH a radius the location is the CENTRE and no text
                    // filter. Both at once would mean "within 25 km AND the word
                    // has to occur" — and would exclude exactly the neighbouring
                    // places for whose sake somebody chooses a radius.
                    radius is null ? location : string.Empty,
                    ToRemoteMode(query["remote"]),
                    query["q"].ToString(),
                    Guid.TryParse(query["company"], out var company) ? company : null,
                    query["employment"].ToString(),
                    radius),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new PageResponse<JobV1>(
                    [.. page.Items.Select(ToContract)],
                    PageSelection.Page(query["page"]),
                    PageSelection.Size(query["page_size"]),
                    page.Total,
                    // Only for a radius search. Without it nothing was left out,
                    // and a `0` would claim the question had been asked.
                    radius is null ? null : page.Unlocated),
                cancellationToken);
        });

        // The place for a point — the reverse direction of the radius search.
        //
        // Without sign-in, like the list itself: the answer comes from a table
        // that travels in the image (ADR-0032). It says nothing about anybody,
        // and the point that comes in is the same already rounded one that also
        // goes to the search.
        jobs.MapGet("/place", async (
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var point = Number(context.Request.Query["lat"]) is { } latitude
                        && Number(context.Request.Query["lon"]) is { } longitude
                        && latitude is >= -90 and <= 90
                        && longitude is >= -180 and <= 180
                ? new PlacePoint(latitude, longitude)
                : (PlacePoint?)null;

            // No place nearby is no error but information: the point lies outside
            // DE, AT and CH. A 404 would be misleading here — the address exists.
            await context.Response.WriteAsJsonAsync(
                new { location = point is { } p ? PlaceLookup.NearestPlace(p) : null },
                cancellationToken);
        });

        // Readable without sign-in, just like the list — and for the same two
        // reasons. The careers page is an address one passes on: whoever opens
        // it has no account, and a 401 would turn the advert into a sign-in
        // prompt. And applications-service asks here service-to-service whether
        // the job exists — without a token, because it has none; the gate
        // therefore ended EVERY application with 503.
        //
        // That is safe without further ado: the handler hands out a job only
        // when it belongs to the asking company OR is published. Without a token
        // the company is `null`, so only the published remains — draft, closed
        // and non-existent are indistinguishably 404.
        jobs.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var job = await mediator.Send(
                new JobQuery(
                    id,
                    principal.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null),
                cancellationToken);

            await Show(context, job, cancellationToken);
        });

        app.MapGet("/companies/me/jobs", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!ActingCompany(principal, out var company))
            {
                await NoCompany(context, principal);
                return;
            }

            var mine = await mediator.Send(new MyJobsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(ToContract), cancellationToken);
        });

        return app;
    }

    private static async Task Transition(
        Guid id,
        JobState target,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!ActingCompany(principal, out var company))
        {
            await NoCompany(context, principal);
            return;
        }

        await Answer(context, cancellationToken, async () =>
        {
            var job = await mediator.Send(
                new ChangeStateCommand(company, id, target), cancellationToken);

            await Show(context, job, cancellationToken);
        });
    }

    /// <summary>
    /// Shows it, or answers identically for "does not exist" and "belongs to
    /// another company".
    /// </summary>
    /// <remarks>
    /// A difference would betray which adverts exist elsewhere — and a draft is
    /// exactly what a company does not want to show yet.
    /// </remarks>
    private static Task Show(HttpContext context, Job? job, CancellationToken cancellationToken) =>
        job is null
            ? ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status404NotFound, "Request failed", "no such job")
            : context.Response.WriteAsJsonAsync(ToContract(job), cancellationToken);

    private static async Task Answer(
        HttpContext context, CancellationToken cancellationToken, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (TransitionNotAllowed error)
        {
            // 409, not 422: the caller may do this in principle, only this
            // advert is not in that state right now.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status409Conflict, "Request failed", error.Message);
        }
        catch (InputError error)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status422UnprocessableEntity,
                "Request failed", error.Message);
        }

        _ = cancellationToken;
    }

    private static bool ActingCompany(ICurrentPrincipal principal, out TenantId company)
    {
        if (principal.Current?.Acting is Capacity.ForCompany forCompany)
        {
            company = forCompany.Tenant;
            return true;
        }

        company = TenantId.None;
        return false;
    }

    private static Task NoCompany(HttpContext context, ICurrentPrincipal principal) =>
        principal.Current is null
            ? NotSignedIn(context)
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden, "Request failed", "no active company");

    private static JobDetails Details(WriteJobV1 body) => new(
        body.Title, body.Description, body.Location, body.PostalCode,
        ToRemoteMode(body.RemoteMode) ?? RemoteMode.None,
        ToEmployment(body.EmploymentType),
        body.Skills ?? []);

    // The words below are the wire contract with web/ and stay byte-identical.

    private static RemoteMode? ToRemoteMode(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "none" => RemoteMode.None,
        "hybrid" => RemoteMode.Hybrid,
        "full" => RemoteMode.Full,
        _ => null
    };

    /// <summary>A number from the query, or <c>null</c>.</summary>
    /// <remarks>
    /// <c>InvariantCulture</c> is not cosmetic: an address bar carries
    /// <c>52.52</c>, and a server with a German locale would read the point as a
    /// thousands separator without this argument — 52.52 would become 5252, and
    /// the radius search would show nothing without reporting an error.
    /// </remarks>
    private static double? Number(string? raw) =>
        double.TryParse(
            raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static EmploymentKind ToEmployment(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "part_time" => EmploymentKind.PartTime,
        "contract" => EmploymentKind.Contract,
        "internship" => EmploymentKind.Internship,
        _ => EmploymentKind.FullTime
    };

    private static JobV1 ToContract(Job job) => new(
        job.Id,
        job.Company.Value,
        job.Title,
        job.Description,
        job.Location,
        job.PostalCode,
        // The member names ARE the wire words: None/Hybrid/Full → "none"/
        // "hybrid"/"full", Draft/Published/Closed → "draft"/"published"/"closed".
        job.Remote.ToString().ToLowerInvariant(),
        job.Employment switch
        {
            EmploymentKind.PartTime => "part_time",
            EmploymentKind.Contract => "contract",
            EmploymentKind.Internship => "internship",
            _ => "full_time"
        },
        job.Skills.Values,
        job.State.ToString().ToLowerInvariant(),
        job.PublishedAt,
        job.UpdatedAt);

    private static Task NotSignedIn(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
