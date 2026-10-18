using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Jobs.Api.Authorization;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Sources;
using WorkerTransfer.Jobs.Contracts;
using WorkerTransfer.Jobs.Domain.Sources;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>/jobs/sources</c> — a company's ATS boards and own feed (ADR-0052).</summary>
/// <remarks>
/// Reading the list is every member's; setting up, fetching and removing are
/// an admin's — a source in "direct" publishes in the company's name.
/// </remarks>
public static class SourceEndpoints
{
    /// <summary>Maps the five routes of the sources.</summary>
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var sources = app.MapGroup("/jobs/sources");
        sources.AddEndpointFilter(async (call, next) =>
        {
            try
            {
                return await next(call);
            }
            catch (SourceSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "identity-service did not answer");
                return Results.Empty;
            }
        });

        sources.MapGet("", async (IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            var list = await mediator.Send(new SourcesQuery(company), cancellationToken);
            await context.Response.WriteAsJsonAsync(list.Select(ToWire), cancellationToken);
        });

        sources.MapPost("", async (WriteSourceV1? body, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            if (SourceWords.ReadKind(body?.Kind) is not { } kind || SourceWords.ReadMode(body?.Mode ?? "draft") is not { } mode)
            {
                await Unprocessable(context, "kind must be greenhouse, lever, personio or feed, and mode draft or direct");
                return;
            }

            var checkedSource = await mediator.Send(
                new CheckSourceQuery(company, kind, body?.Key, mode), cancellationToken);

            if (checkedSource is not SourceResult.Done done)
            {
                await Unprocessable(context, ((SourceResult.Invalid)checkedSource).Reason);
                return;
            }

            var saved = await mediator.Send(new SaveSourceCommand(done.Source), cancellationToken);
            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(ToWire(saved), cancellationToken);
        })
        .RequireAuthorization(AdminPermissionExtensions.Policy(JobPermissions.Sources));

        sources.MapPut("/{id:guid}", async (Guid id, SourceModeV1? body, IMediator mediator,
            ICurrentPrincipal principal, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            if (SourceWords.ReadMode(body?.Mode) is not { } mode)
            {
                await Unprocessable(context, "mode must be draft or direct");
                return;
            }

            if (await mediator.Send(new SetSourceModeCommand(company, id, mode), cancellationToken) is not SourceResult.Done done)
            {
                await NotFound(context);
                return;
            }

            await context.Response.WriteAsJsonAsync(ToWire(done.Source), cancellationToken);
        })
        .RequireAuthorization(AdminPermissionExtensions.Policy(JobPermissions.Sources));

        sources.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            if (!await mediator.Send(new DeleteSourceCommand(company, id), cancellationToken))
            {
                await NotFound(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        })
        .RequireAuthorization(AdminPermissionExtensions.Policy(JobPermissions.Sources));

        // Read first, write second: no transaction waits for the ATS.
        sources.MapPost("/{id:guid}/fetch", async (Guid id, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Company(context, principal) is not { } company)
            {
                return;
            }

            if (await mediator.Send(new ReadSourceQuery(id, company), cancellationToken) is not { } read
                || await mediator.Send(new ApplySourceCommand(id, read.Read), cancellationToken) is not { } outcome)
            {
                await NotFound(context);
                return;
            }

            await context.Response.WriteAsJsonAsync(
                new FetchV1(ToWire(outcome.Source), outcome.Added, outcome.Changed, outcome.Closed), cancellationToken);
        })
        .RequireAuthorization(AdminPermissionExtensions.Policy(JobPermissions.Sources));

        return app;
    }

    private static SourceV1 ToWire(JobSource source) => new(
        source.Id,
        SourceWords.Word(source.Kind),
        source.Key,
        SourceWords.Word(source.Mode),
        source.ProvenAt,
        source.FetchedAt,
        source.Outcome,
        source.IsProven ? null : source.Code);

    private static async Task<TenantId?> Company(HttpContext context, ICurrentPrincipal principal)
    {
        if (principal.Current is not { } actor)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
            return null;
        }

        if (actor.Acting is not Capacity.ForCompany company)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden, "Request failed", "sources require an active company");
            return null;
        }

        return company.Tenant;
    }

    private static Task Unprocessable(HttpContext context, string reason) =>
        ProblemDetailsMiddleware.Write(context, StatusCodes.Status422UnprocessableEntity, "Request failed", reason);

    private static Task NotFound(HttpContext context) =>
        ProblemDetailsMiddleware.Write(context, StatusCodes.Status404NotFound, "Request failed", "No such source");
}
