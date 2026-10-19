using WorkerTransfer.Transfer.Api.Authorization;
using WorkerTransfer.ServiceDefaults.Roles;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Requests;
using WorkerTransfer.Transfer.Application.Market;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Contracts;
using WorkerTransfer.Transfer.Domain.Requests;
using WorkerTransfer.Transfer.Domain.Market;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/market/*</c> — the status, its release and its revocation.</summary>
/// <remarks>
/// There is no <c>:public</c> here, and there must not be one. On the profile
/// "to every company" is a sensible choice; here it would be a switch whose
/// consequences nobody can survey — among them one's own employer, who is on
/// the same platform.
/// </remarks>
public static class MarketEndpoints
{
    /// <summary>Maps the eight routes of the market.</summary>
    public static IEndpointRouteBuilder MapMarketEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var market = Responses.Guarded(app.MapGroup("/market"));

        market.MapPut("/me", async (
            WriteMarketStatusV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            if (Availabilities.Read(body.Availability) is not { } availability)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "That is not an availability");
                return;
            }

            try
            {
                // Whose status it is comes from the verified token and never
                // from the body: a subject id on the wire would be a way to set
                // somebody else's market status.
                var status = await mediator.Send(
                    new SaveMarketStatusCommand(
                        actor.Subject, availability, body.Employed, body.Note,
                        body.OpenFrom, body.OpenUntil),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    Responses.ToWire(status, Responses.Today(context), own: true), cancellationToken);
            }
            catch (MarketError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        // Never `null`: "said nothing" IS a state, namely `unavailable`. A
        // `null` would force the interface to invent a default, and the danger
        // is that it invents the wrong one.
        market.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            var status = await mediator.Send(
                new MyMarketStatusQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Responses.ToWire(status, Responses.Today(context), own: true), cancellationToken);
        });

        // Who asked — and what holds right now. `active` comes fresh from the
        // ledger and may differ from the state: after a revocation GRANTED
        // stays standing, `active` falls to false.
        market.MapGet("/me/requests", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            var mine = await mediator.Send(
                new MyRequestsQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(view => Responses.ToWire(view.MarketRequest, view.Active)),
                cancellationToken);
        });

        // Refused ones stay visible too. Otherwise "refused" and "never asked"
        // would look the same — and then somebody asks again, in good faith.
        market.MapGet("/requests", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var requests = await mediator.Send(
                new CompanyRequestsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                requests.Select(request => Responses.ToWire(request, active: null)),
                cancellationToken);
        });

        market.MapPost("/requests/{id:guid}/grant", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Answer(id, grant: true, mediator, principal, context, cancellationToken));

        market.MapPost("/requests/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Answer(id, grant: false, mediator, principal, context, cancellationToken));

        // The revocation takes effect in the ledger, not in the case. A running
        // transfer stays: it has a door and a refusal of its own.
        market.MapPost("/requests/{id:guid}/revoke", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            var result = await mediator.Send(
                new RevokeAccessCommand(id, actor.Subject), cancellationToken);

            await Responses.Write(context, result, cancellationToken, active: false);
        });

        // "May I see whether you are listening right now?" — the easier of the
        // two questions. The harder one is the case itself.
        market.MapPost("/{subjectId:guid}/requests", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var result = await mediator.Send(
                new RequestMarketStatusCommand(
                    new SubjectId(subjectId), company, actor.Subject),
                cancellationToken);

            // The asking company gets no `active`: it has the answer already in
            // the form of the status it sees or does not see.
            await Responses.Write(
                context, result, cancellationToken,
                active: null, successStatus: StatusCodes.Status201Created);
        })
        // A first contact to a person who did not come forward: at least a
        // `recruiter` (ADR-0048), resolved from identity-service's membership
        // table per request. The check stands BEFORE the body and the person
        // are looked at.
        .RequireAuthorization(AdminPermissionExtensions.Policy(MarketPermissions.Request));

        // The notice period (VISION-FRAGEN H2) — the person's own, and there is
        // no route by which anybody else reads it.
        market.MapGet("/me/notice", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            var notice = await mediator.Send(new MyNoticeQuery(actor.Subject), cancellationToken);
            await context.Response.WriteAsJsonAsync(
                Responses.ToWire(notice, Responses.Today(context)), cancellationToken);
        });

        market.MapPut("/me/notice", async (
            WriteNoticeV1? body, IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            if (body is null
                || NoticeWords.ReadUnit(body.Unit) is not { } unit
                || NoticeWords.ReadEnd(body.End) is not { } end)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "unit and end are required");
                return;
            }

            try
            {
                var notice = await mediator.Send(
                    new StateNoticeCommand(actor.Subject, body.Length, unit, end), cancellationToken);
                await context.Response.WriteAsJsonAsync(
                    Responses.ToWire(notice, Responses.Today(context)), cancellationToken);
            }
            catch (MarketError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity, "Request failed", error.Message);
            }
        });

        market.MapDelete("/me/notice", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            await mediator.Send(new WithdrawNoticeCommand(actor.Subject), cancellationToken);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        market.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var status = await mediator.Send(
                new VisibleMarketStatusQuery(new SubjectId(subjectId), company),
                cancellationToken);

            if (status is null)
            {
                // Not present, not released — the same answer. The difference
                // would be especially expensive here: the mere existence of the
                // statement gives something away.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;
            }

            await context.Response.WriteAsJsonAsync(Responses.ToWire(status, Responses.Today(context), own: false), cancellationToken);
        });

        return app;
    }

    private static async Task Answer(
        Guid id,
        bool grant,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (principal.Current is not { } actor)
        {
            await Responses.NotAuthenticated(context);
            return;
        }

        var result = await mediator.Send(
            new AnswerRequestCommand(id, actor.Subject, grant), cancellationToken);

        await Responses.Write(context, result, cancellationToken, active: grant);
    }
}
