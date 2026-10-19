using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Api.Authorization;
using WorkerTransfer.Transfer.Application.Employers;
using WorkerTransfer.Transfer.Application.Releases;
using WorkerTransfer.Transfer.Contracts;

namespace WorkerTransfer.Transfer.Api;

/// <summary>The way to the current employer — two consents, and a negotiation between companies (ADR-0050).</summary>
/// <remarks>
/// <para><strong>Every refusal about a company's reach is one 404.</strong>
/// No route, a route to somebody else, an employer that does not let itself be
/// asked, a revoked consent: told apart, each would tell a company something
/// about where a person works.</para>
///
/// <para>The person's own steps take her subject from the token whatever she
/// acts as — like <c>/transfers/{id}/decline</c>: a negotiation about her is
/// hers to leave.</para>
/// </remarks>
public static class ReleaseEndpoints
{
    /// <summary>Maps the fourteen routes of the way to the employer.</summary>
    public static IEndpointRouteBuilder MapReleaseEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        MapRoute(Responses.Guarded(app.MapGroup("/market/me/employer")));

        var transfers = Responses.Guarded(app.MapGroup("/transfers"));

        MapSetting(transfers);

        // What other companies asked about the own staff — a list only of
        // inquiries that were made, never of who opened a route.
        transfers.MapGet("/employer/inquiries", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var views = await mediator.Send(new EmployerInquiriesQuery(company), cancellationToken);
            await context.Response.WriteAsJsonAsync(views.Select(Responses.ToWire), cancellationToken);
        });

        transfers.MapGet("/{id:guid}/release", async (
            Guid id, IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            Viewer viewer = actor.Acting is Capacity.ForCompany company
                ? new Viewer.Company(company.Tenant)
                : new Viewer.Person(actor.Subject);

            await Responses.Write(context, await mediator.Send(new ReleaseForCaseQuery(id, viewer), cancellationToken),
                cancellationToken);
        });

        transfers.MapPost("/{id:guid}/release", async (
            Guid id, ReleaseTermsV1? body, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var result = await mediator.Send(
                new AskReleaseCommand(id, company, body?.ReleaseMonth, body?.FeeCents), cancellationToken);

            await Responses.Write(context, result, cancellationToken, StatusCodes.Status201Created);
        })
        // A month and a fee: it binds the company. Checked BEFORE the transfer
        // is looked at, so a member learns nothing from the difference.
        .RequireAuthorization(AdminPermissionExtensions.Policy(CasePermissions.Release));

        var releases = transfers.MapGroup("/releases");

        releases.MapPost("/{id:guid}/terms", (
                Guid id, ReleaseTermsV1? body, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            CompanyStep(id, ReleaseMove.Terms, body, mediator, principal, context, cancellationToken))
            .RequireAuthorization(AdminPermissionExtensions.Policy(CasePermissions.Release));

        releases.MapPost("/{id:guid}/accept", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            CompanyStep(id, ReleaseMove.Accept, null, mediator, principal, context, cancellationToken))
            .RequireAuthorization(AdminPermissionExtensions.Policy(CasePermissions.Release));

        // NO permission on saying no: whoever may be asked must be able to
        // refuse, and whoever may ask must be able to stop asking.
        releases.MapPost("/{id:guid}/refuse", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            CompanyStep(id, ReleaseMove.Refuse, null, mediator, principal, context, cancellationToken));

        releases.MapPost("/{id:guid}/withdraw", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            CompanyStep(id, ReleaseMove.Withdraw, null, mediator, principal, context, cancellationToken));

        releases.MapPost("/{id:guid}/agree", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            PersonStep(id, ReleaseMove.Agree, mediator, principal, context, cancellationToken));

        // Always possible while it runs — also because she does not want to
        // move at all.
        releases.MapPost("/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            PersonStep(id, ReleaseMove.Decline, mediator, principal, context, cancellationToken));

        return app;
    }

    /// <summary><c>/market/me/employer</c> — naming the employer IS the consent.</summary>
    private static void MapRoute(RouteGroupBuilder route)
    {
        // Never null: "no route" is a state, answered with empty fields.
        route.MapGet("", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            var view = await mediator.Send(new MyEmployerRouteQuery(actor.Subject), cancellationToken);
            await context.Response.WriteAsJsonAsync(ToWire(view), cancellationToken);
        });

        route.MapPut("", async (
            OpenEmployerRouteV1? body, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            if (body is null || body.EmployerId == Guid.Empty)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity, "Request failed", "employer_id is required");
                return;
            }

            // Whose route it is comes from the verified token: a subject on the
            // wire would let somebody tell an employer about another human being.
            var result = await mediator.Send(
                new OpenEmployerRouteCommand(actor.Subject, new TenantId(body.EmployerId)), cancellationToken);

            if (result is not RouteResult.Done done)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such company");
                return;
            }

            await context.Response.WriteAsJsonAsync(ToWire(done.View), cancellationToken);
        });

        route.MapDelete("", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await Responses.NotAuthenticated(context);
                return;
            }

            await mediator.Send(new CloseEmployerRouteCommand(actor.Subject), cancellationToken);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
    }

    /// <summary><c>/transfers/employer/setting</c> — the company's own switch, read by any member.</summary>
    private static void MapSetting(RouteGroupBuilder transfers)
    {
        transfers.MapGet("/employer/setting", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var accepts = await mediator.Send(new EmployerSettingQuery(company), cancellationToken);
            await context.Response.WriteAsJsonAsync(new EmployerSettingV1(accepts), cancellationToken);
        });

        transfers.MapPut("/employer/setting", async (
            EmployerSettingV1? body, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            if (body is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity, "Request failed", "accepts_inquiries is required");
                return;
            }

            var setting = await mediator.Send(
                new SetEmployerSettingCommand(company, body.AcceptsInquiries), cancellationToken);
            await context.Response.WriteAsJsonAsync(new EmployerSettingV1(setting.AcceptsInquiries), cancellationToken);
        })
        .RequireAuthorization(AdminPermissionExtensions.Policy(CasePermissions.Inquiries));
    }

    private static EmployerRouteV1 ToWire(RouteView? view) =>
        view is null
            ? new EmployerRouteV1(null, false, null)
            : new EmployerRouteV1(view.Route.Employer.Value, view.Open, view.Route.OpenedAt);

    private static async Task CompanyStep(
        Guid id,
        ReleaseMove move,
        ReleaseTermsV1? body,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await Responses.Company(context, principal) is not { } company)
        {
            return;
        }

        var result = await mediator.Send(
            new MoveReleaseCommand(id, new Viewer.Company(company), move, body?.ReleaseMonth, body?.FeeCents),
            cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }

    private static async Task PersonStep(
        Guid id,
        ReleaseMove move,
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
            new MoveReleaseCommand(id, new Viewer.Person(actor.Subject), move), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }
}
