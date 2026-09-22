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

        var cases = Responses.Abgesichert(app.MapGroup("/transfers"));

        // A company shows interest. Precondition: the market status is released
        // to this company AND the person is approachable. `unavailable` means
        // no, even with a release — the release permits seeing, not disturbing.
        cases.MapPost("/", async (
            InteresseZeigenV1 body,
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

            // Ohne `subjectId` gibt es niemanden, an dem Interesse bestuende.
            // Der leere Guid lief frueher bis in `new SubjectId(...)` und kam
            // als 500 zurueck (D2) — eine kaputte ResumeRequest, keine kaputte
            // Anwendung.
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

            var meine = await mediator.Send(
                new MyCasesQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Responses.Zu), cancellationToken);
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

            var ihre = await mediator.Send(new CompanyCasesQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                ihre.Select(Responses.Zu), cancellationToken);
        });

        cases.MapPost("/{id:guid}/accept-talk", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.AcceptConversationCommand, mediator, principal, context,
                cancellationToken));

        cases.MapPost("/{id:guid}/accept-offer", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.AcceptOfferCommand, mediator, principal, context,
                cancellationToken));

        // The person confirms that their employer lets them go. The platform
        // does not check that and cannot — it knows neither the employer nor
        // the contract. What the step achieves is asking the question and
        // recording the answer.
        cases.MapPost("/{id:guid}/confirm-release", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.ConfirmReleaseCommand, mediator, principal, context,
                cancellationToken));

        // Always possible, from every running state. A process one cannot get
        // out of is no process but a trap.
        cases.MapPost("/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.Absagen, mediator, principal, context, cancellationToken));

        cases.MapPost("/{id:guid}/offer", async (
            Guid id,
            AngebotV1 body,
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
                new MakeOfferCommand(id, company, body.Note, body.StartOn, body.FeeCents),
                cancellationToken);

            await Responses.Write(context, result, cancellationToken);
        })
        // Ein Angebot nennt Eintrittstermin und Gebuehr — es bindet das
        // Unternehmen, und darum verlangt es einen `admin`. Aufgeloest aus der
        // Mitgliedschaftstabelle von identity-service, nie aus dem Token.
        .RequireAuthorization(AdminPermissionExtensions.Richtlinie(CasePermissions.Offer));

        cases.MapPost("/{id:guid}/complete", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Company(id, CompanySwitch.Complete, mediator, principal, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Richtlinie(CasePermissions.Complete));

        // KEIN Firmenrecht: wer einen Case anfangen darf, muss ihn beenden
        // duerfen. Sonst waere die Einladung eine Falle — derselbe Satz, den
        // `/decline` fuer die Person schon traegt.
        cases.MapPost("/{id:guid}/withdraw", (
                Guid id, IMediator mediator, ICurrentPrincipal principal,
                HttpContext context, CancellationToken cancellationToken) =>
            Company(id, CompanySwitch.WithdrawCommand, mediator, principal, context, cancellationToken));

        return app;
    }

    private static async Task Person(
        Guid id,
        Personenzug zug,
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
            new PersonMoveCommand(id, actor.Subject, zug), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }

    private static async Task Company(
        Guid id,
        CompanySwitch zug,
        IMediator mediator,
        ICurrentPrincipal principal,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await Responses.Company(context, principal) is not { } company)
        {
            return;
        }

        var result = await mediator.Send(new CompanySwitchCommand(id, company, zug), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }
}
