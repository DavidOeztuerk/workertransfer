using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Applications;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary>The routes of the application.</summary>
/// <remarks>
/// None carries a score, a rank or an ordering by fit. How well somebody fits a
/// job is computed in the browser, shown to the <em>person</em> and never to the
/// company (ADR-0022); a list of applicants this service ranked would be
/// candidate rating through the back door.
/// </remarks>
public static class ApplicationEndpoints
{
    /// <summary>Maps all routes of this service's applications.</summary>
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var applications = Guarded(app.MapGroup("/applications"));
        var jobs = Guarded(app.MapGroup("/jobs"));
        var companies = Guarded(app.MapGroup("/companies"));

        // Applying — and with it releasing the data to this one company. The
        // release comes about in the ledger, not in this database, and it names
        // the recipient.
        applications.MapPost("/", async (
            SubmitApplicationV1 body,
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

            ArgumentNullException.ThrowIfNull(body);

            // Who applies stands in the verified token and never in the body.
            var result = await mediator.Send(
                new SubmitApplicationCommand(
                    body.JobId, actor.Subject, body.Message,
                    body.SharesResume, body.SharesPortfolio),
                cancellationToken);

            await Answer(context, result, cancellationToken, StatusCodes.Status201Created);
        });

        applications.MapGet("/me", async (
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

            var mine = await mediator.Send(
                new MyApplicationsQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(ToContract), cancellationToken);
        });

        applications.MapGet("/{id:guid}", async (
            Guid id,
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

            TenantId? company = actor.Acting is Capacity.ForCompany forCompany
                ? forCompany.Tenant
                : null;

            var application = await mediator.Send(
                new ReadApplicationQuery(id, company, actor.Subject),
                cancellationToken);

            if (application is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such application");
                return;
            }

            await context.Response.WriteAsJsonAsync(ToContract(application), cancellationToken);
        });

        // Withdrawing — the case stays, the data is gone. That somebody applied
        // and withdrew belongs to the history of the process in the company; the
        // person behind it can no longer be seen afterwards.
        applications.MapPost("/{id:guid}/withdraw", async (
            Guid id,
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

            var result = await mediator.Send(
                new WithdrawApplicationCommand(id, actor.Subject), cancellationToken);

            await Answer(context, result, cancellationToken);
        });

        applications.MapPost("/{id:guid}/status", async (
            Guid id,
            MoveApplicationV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var result = await mediator.Send(
                new MoveApplicationCommand(id, company, body.Status), cancellationToken);

            await Answer(context, result, cancellationToken);
        });

        // The lambda parameter is bound by name to `{job}` in the template.
        jobs.MapGet("/{job:guid}/applications", async (
            Guid job,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            var found = await mediator.Send(
                new ApplicationsForJobQuery(job, company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                found.Select(ToContract), cancellationToken);
        });

        // Numbers about one's OWN cases. The company sees them one by one
        // anyway; the sum over them is a convenience, no new information. What
        // does not exist here and is not to exist is the merging with market
        // status, résumé or cases at other companies (ADR-0022/0026).
        companies.MapGet("/me/application-stats", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            var numbers = await mediator.Send(new CompanyCountsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new ApplicationCountsV1(
                    numbers.ToDictionary(
                        entry => ApplicationStates.Word(entry.Key),
                        entry => entry.Value),
                    numbers.Values.Sum()),
                cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// What a filter returns that has written the answer itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write the answer once more — as a JSON <c>null</c>, headers
    /// included, and those already stand at that point. What comes out is no
    /// 500 but a torn connection: the caller gets "Error while copying content
    /// to a stream" and never the 503 it is due. Measured at exactly this route,
    /// which for two days looked like a broken body.
    /// </remarks>
    private static readonly IResult Written = Results.Empty;

    /// <summary>Attaches the 503 translation to a group.</summary>
    /// <remarks>
    /// A silent ledger — and a silent jobs-service — are neither a yes nor a no.
    /// Answering 404 would claim the job does not exist; creating the
    /// application anyway would presume a release nobody confirmed. 503 says the
    /// one true thing: this service cannot answer right now.
    /// <para>
    /// As a filter per group instead of a <c>catch</c> per route — and on
    /// <em>all</em> groups, including those that touch neither service today.
    /// The route that forgets it looks like a working one until a dependency
    /// fails.
    /// </para>
    /// </remarks>
    private static RouteGroupBuilder Guarded(RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (call, next) =>
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
            catch (JobLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a dependency is unavailable");

                return Written;
            }
            catch (CompanyLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "identity-service did not answer");

                return Written;
            }
        });

        return group;
    }

    /// <summary>The caller's company, or a refusal already written.</summary>
    private static async Task<TenantId?> Company(HttpContext context, ICurrentPrincipal principal)
    {
        if (principal.Current is not { } actor)
        {
            await NotSignedIn(context);
            return null;
        }

        if (actor.Acting is not Capacity.ForCompany company)
        {
            // A statement about the caller that gives nothing away: applications
            // are read by whoever acts for a company, and a private person knows
            // she is none.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "reading applications requires an active company");

            return null;
        }

        return company.Tenant;
    }

    /// <summary>
    /// Translates the domain's results into status codes — in one place, so four
    /// routes do not drift apart.
    /// </summary>
    private static async Task Answer(
        HttpContext context,
        ApplicationResult result,
        CancellationToken cancellationToken,
        int success = StatusCodes.Status200OK)
    {
        switch (result)
        {
            case ApplicationResult.Done done:
                context.Response.StatusCode = success;
                await context.Response.WriteAsJsonAsync(
                    ToContract(done.Application), cancellationToken);
                return;

            case ApplicationResult.NoJob:
                // Draft, closed and missing stay indistinguishable — as
                // jobs-service itself keeps them.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such job");
                return;

            case ApplicationResult.Unknown:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such application");
                return;

            case ApplicationResult.StateConflict conflict:
                // The input is fine, the state does not fit.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", conflict.Reason);
                return;

            case ApplicationResult.InvalidInput input:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", input.Reason);
                return;

            default:
                throw new InvalidOperationException($"Unhandled result: {result}");
        }
    }

    private static ApplicationV1 ToContract(JobApplication application) =>
        new(application.Id,
            application.Job,
            application.Company.Value,
            application.Subject.Value,
            application.Message,
            application.Enclosures.CurriculumVitae,
            application.Enclosures.Portfolio,
            application.Enclosures.Documents,
            ApplicationStates.Word(application.State),
            application.CreatedAt,
            application.UpdatedAt,
            new ApplicantContactV1(
                application.Contact.FullName,
                application.Contact.Line1,
                application.Contact.Line2,
                application.Contact.PostalCode,
                application.Contact.City,
                application.Contact.Country,
                application.Contact.Phone,
                application.Contact.Email));

    private static Task NotSignedIn(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
