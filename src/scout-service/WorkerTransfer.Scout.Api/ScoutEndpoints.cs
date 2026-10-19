using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Approach;
using WorkerTransfer.Scout.Application.Candidates;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Application.Searches;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Api;

/// <summary><c>/scout</c> — search, save, approach.</summary>
/// <remarks>
/// <para><strong>A company is required everywhere.</strong> Whoever acts for
/// themselves gets 403 "no active company" — a statement about the
/// <em>caller</em> that reveals nothing about those searched for. That is the
/// hard part of <c>GET /candidates</c>, carried over and not reinvented.</para>
///
/// <para><strong>And <c>member</c> is enough, no <c>admin</c>.</strong>
/// Searching, drafting and approaching is the daily work in the company's name;
/// a right that is too narrow would turn an invitation into a spectator seat,
/// and then somebody creates a second admin in order to work.</para>
/// </remarks>
public static class ScoutEndpoints
{
    /// <summary>Maps the scout routes.</summary>
    public static IEndpointRouteBuilder MapScoutEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A silent ledger is neither a yes nor a no, and a silent profile search
        // is not either. 404 would say there is nobody; an empty list would claim
        // the same more quietly. 503 says the only true thing: this service
        // cannot answer right now.
        //
        // As a filter instead of a catch in every route, because the route that
        // forgets it looks like a working one until something fails.
        var scout = app.MapGroup("/scout").AddEndpointFilter(CatchSilence);

        scout.MapGet("/candidates", async (
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            var query = context.Request.Query;

            SearchFilter filter;

            try
            {
                filter = SearchFilter.From(
                    query["skill"].Count > 0 ? [.. query["skill"]!] : null,
                    query["location"].ToString(),
                    query["remote"] == "true");
            }
            catch (InputError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
                return;
            }

            // `stelle` DOES NOT FILTER. It adds a piece of information to each hit
            // and takes none away (ADR-0041): whoever lives further away stays in
            // the list and carries a cross. The company decides, not the search —
            // and it sees why.
            //
            // The key `stelle` is German and stays so: it is the query parameter
            // web/ sends (`?stelle=<id>`), a wire contract and not an identifier.
            var page = await mediator.Send(
                new CandidatesQuery(
                    company, filter, PageLength(query["limit"]), query["cursor"],
                    Guid.TryParse(query["stelle"], out var job) ? job : null),
                cancellationToken);

            // THE ONE PLACE WHERE A READ WRITES SOMETHING — and it stands here
            // deliberately visible instead of hidden in the query.
            //
            // Whoever turns up in a search has been discovered, and ADR-0033 says:
            // the person learns of it. Not through a log "who looked at whom" —
            // that would be the most sensitive table of the system — but through a
            // message that names NO company.
            //
            // As a COMMAND of its own and not in the query, because only a command
            // gets a transaction scope: the outbox row has to be committed together
            // with its cause or not at all (ADR-0025).
            if (page.Items.Count > 0)
            {
                await mediator.Send(
                    new ReportDiscoveryCommand([.. page.Items.Select(hit => hit.Subject)]),
                    cancellationToken);
            }

            // No total (ADR-0026): through the difference to the page length it
            // would reveal how many profiles are NOT released.
            await context.Response.WriteAsJsonAsync(
                new HitPageV1([.. page.Items.Select(ToContract)], page.Next),
                cancellationToken);
        });

        scout.MapPost("/candidates/{subjectId:guid}/draft", async (
            Guid subjectId,
            ApproachRequestV1? body,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            try
            {
                // The searched words run through the same filter as the search:
                // the same vocabulary, the same limits — and thus here too no way
                // of slipping in a piece of evidence.
                var searched = SearchFilter.From(body?.Skills, null, false);

                var draft = await mediator.Send(
                    new ApproachQuery(
                        company,
                        new SubjectId(subjectId),
                        searched.NamedWords,
                        body?.Wish ?? string.Empty),
                    cancellationToken);

                // Only the text. No `sent_at`, no recipient, no identifier of a
                // case: there is no case, because nothing was sent (ADR-0036
                // condition 4).
                await context.Response.WriteAsJsonAsync(
                    new ApproachDraftV1(draft), cancellationToken);
            }
            catch (NoContactPerson error)
            {
                // Hidden and not present answer alike, and that has to stay so: a
                // difference would say whether this human being is here.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", error.Message);
            }
            catch (DraftUnavailable error)
            {
                // The kind of the failure, never its content — and expressly no
                // template that would look like a suggestion.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", error.Message);
            }
            catch (InputError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        scout.MapPost("/searches", async (
            CreateSearchV1? body,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            try
            {
                var filter = SearchFilter.From(
                    body?.Skills, body?.Location, body?.Remote ?? false);

                var search = await mediator.Send(
                    new SaveSearchCommand(
                        company, actor.Current!.Subject, body?.Name ?? string.Empty, filter),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(ToContract(search), cancellationToken);
            }
            catch (InputError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        scout.MapGet("/searches", async (
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            var searches = await mediator.Send(
                new MySearchesQuery(company, actor.Current!.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["items"] = searches.Select(ToContract).ToArray()
                },
                cancellationToken);
        });

        scout.MapDelete("/searches/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal actor,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Company(actor) is not { } company)
            {
                await WithoutCompany(actor, context);
                return;
            }

            var removed = await mediator.Send(
                new DeleteSearchCommand(id, company, actor.Current!.Subject), cancellationToken);

            if (!removed)
            {
                // 404 for a colleague's search too: a difference would reveal that
                // it exists.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such search");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    /// <summary>The company from the token — never from the request.</summary>
    /// <remarks>
    /// The tenant comes from the verified token (ADR-0017/0018). Taking it from a
    /// header or a field would mean letting the caller decide in whose name they
    /// search.
    /// </remarks>
    private static TenantId? Company(ICurrentPrincipal actor) =>
        actor.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    private static Task WithoutCompany(ICurrentPrincipal actor, HttpContext context) =>
        actor.Current is null
            ? ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated")
            // 403 is a statement about the caller and reveals nothing about the
            // human beings they ask about.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    /// <summary>
    /// What a filter returns that has written the answer itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> after headers that are
    /// already sent at that point. For a request with a body the connection then
    /// tears.
    /// </remarks>
    private static readonly IResult Written = Results.Empty;

    private static async ValueTask<object?> CatchSilence(
        EndpointFilterInvocationContext call, EndpointFilterDelegate next)
    {
        try
        {
            return await next(call);
        }
        catch (ConsentGateSilent)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the consent ledger did not answer");

            return Written;
        }
        catch (ProfileSearchSilent)
        {
            // Expressly no empty result: an empty hit list would look like "there
            // is nobody", and that would be a statement about people that stems
            // from our outage.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the profile search did not answer");

            return Written;
        }
    }

    /// <summary>How many rows a page carries.</summary>
    private static int PageLength(string? raw) =>
        int.TryParse(raw, out var value)
        && value > 0
        && value <= CandidatesHandler.MaxPageLength
            ? value
            : CandidatesHandler.DefaultPageLength;

    /// <remarks>
    /// Carries no score, no rank and no percentage — and none belongs on it
    /// (ADR-0022). What stands here the person wrote, or it is evidence with
    /// origin and link.
    /// </remarks>
    private static HitV1 ToContract(Hit hit) =>
        new(
            hit.Subject.Value,
            hit.Headline,
            hit.Bio,
            hit.Location,
            hit.RemoteOk,
            hit.Named,
            [.. hit.Ticks.Select(tick => new TickV1(tick.Word, tick.Named))],
            [
                .. hit.Evidence.Select(evidence => new EvidenceV1(
                    evidence.Word, Word(evidence.Kind), evidence.Project, evidence.Url))
            ],
            Word(hit.EvidenceState),
            AvailabilityTerms.Word(hit.Availability.State),
            AvailabilityTerms.Word(hit.Availability.Commute),
            AvailabilityTerms.Word(hit.Availability.Attendance));

    private static SearchV1 ToContract(Search search) =>
        new(
            search.Id,
            search.Name,
            search.Filter.NamedWords,
            search.Filter.Location,
            search.Filter.RemoteOnly,
            search.CreatedAt);

    /// <summary>The word for the evidence kind, on the wire.</summary>
    private static string Word(EvidenceKind kind) => kind switch
    {
        EvidenceKind.Topic => "topic",
        EvidenceKind.Language => "language",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>The word for the evidence state, on the wire.</summary>
    /// <remarks>
    /// A word and no sentence: the UI phrases it in the reader's language
    /// (ADR-0031), and a German sentence on the wire would be a language the
    /// server fixes for everybody.
    /// </remarks>
    private static string Word(EvidenceState state) => state switch
    {
        EvidenceState.Complete => "complete",
        EvidenceState.NoneReleased => "none_released",
        EvidenceState.Partial => "partial",
        EvidenceState.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
