using WorkerTransfer.Transfer.Application.Market;
using WorkerTransfer.Transfer.Domain.Market;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Api.Authorization;
using WorkerTransfer.Transfer.Application.Cases;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Contracts;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/transfers/*</c> — three yeses, a no at any time.</summary>
/// <remarks>
/// Separate endpoints instead of one <c>PATCH status</c>: every transition
/// belongs to one side, and a shared one would have to work out at every call
/// who may do what just now. Separated, it stands in the address.
/// </remarks>
public static class CaseEndpoints
{
    /// <summary>Maps the nine routes of the cases.</summary>
    public static IEndpointRouteBuilder MapCaseEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var cases = Responses.Guarded(app.MapGroup("/transfers"));

        // A company shows interest. Precondition: the market status is released
        // to this company AND the person is approachable. `unavailable` means
        // no, even with a release — the release permits seeing, not disturbing.
        cases.MapPost("/", async (
            ShowInterestV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            // Without `subjectId` there is nobody to be interested in. The empty
            // Guid used to run all the way into `new SubjectId(...)` and came
            // back as 500 (D2) — a broken request, not a broken application.
            if (body.SubjectId == Guid.Empty)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "subjectId is required");
                return;
            }

            var result = await mediator.Send(
                new ShowInterestCommand(new SubjectId(body.SubjectId), company, body.Message),
                cancellationToken);

            await Responses.Write(
                context, result, cancellationToken, StatusCodes.Status201Created);
        });

        cases.MapGet("/me", async (
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
                new MyCasesQuery(actor.Subject), cancellationToken);

            // Her own view, and only hers: an offer starting before she could
            // leave is marked (H2). The company's list carries no mark.
            // Only while she says she is employed — see `Responses.ToWire(NoticePeriod?, …)`.
            var notice = await mediator.Send(new MyNoticeQuery(actor.Subject), cancellationToken);
            var status = await mediator.Send(new MyMarketStatusQuery(actor.Subject), cancellationToken);
            var earliest = status.IsEmployed ? notice?.EarliestStart(Responses.Today(context)) : null;

            await context.Response.WriteAsJsonAsync(
                mine.Select(caseItem => Responses.ToWire(caseItem, earliest)), cancellationToken);
        });

        cases.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            var theirs = await mediator.Send(new CompanyCasesQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                theirs.Select(caseItem => Responses.ToWire(caseItem)), cancellationToken);
        });

        cases.MapPost("/{id:guid}/accept-talk", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, PersonMove.AcceptConversation, mediator, principal, context,
                cancellationToken));

        cases.MapPost("/{id:guid}/accept-offer", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, PersonMove.AcceptOffer, mediator, principal, context,
                cancellationToken));

        // The person confirms that their employer lets them go. The platform
        // does not check that and cannot — it knows neither the employer nor
        // the contract. What the step achieves is asking the question and
        // recording the answer.
        cases.MapPost("/{id:guid}/confirm-release", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, PersonMove.ConfirmRelease, mediator, principal, context,
                cancellationToken));

        // Always possible, from every running state. A process one cannot get
        // out of is no process but a trap.
        cases.MapPost("/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, PersonMove.Decline, mediator, principal, context, cancellationToken));

        cases.MapPost("/{id:guid}/offer", async (
            Guid id,
            OfferV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Company(context, principal) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var result = await mediator.Send(
                new MakeOfferCommand(id, company, body.Note, body.StartOn),
                cancellationToken);

            await Responses.Write(context, result, cancellationToken);
        })
        // An offer names a start date — it binds the company, and
        // that is why it demands an `admin`. Resolved from the membership table
        // of identity-service, never from the token.
        .RequireAuthorization(AdminPermissionExtensions.Policy(CasePermissions.Offer));

        cases.MapPost("/{id:guid}/complete", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Company(id, CompanyMove.Complete, mediator, principal, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Policy(CasePermissions.Complete));

        // NO company permission: whoever may start a case must be allowed to
        // end it. Otherwise the invitation would be a trap — the same sentence
        // `/decline` already carries for the person.
        cases.MapPost("/{id:guid}/withdraw", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Company(id, CompanyMove.Withdraw, mediator, principal, context, cancellationToken));

        return app;
    }

    private static async Task Person(
        Guid id,
        PersonMove move,
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
            new PersonMoveCommand(id, actor.Subject, move), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }

    private static async Task Company(
        Guid id,
        CompanyMove move,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await Responses.Company(context, principal) is not { } company)
        {
            return;
        }

        var result = await mediator.Send(new CompanyMoveCommand(id, company, move), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }
}
