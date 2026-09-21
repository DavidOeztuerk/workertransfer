using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Api.Berechtigung;
using WorkerTransfer.Transfer.Application.Vorgaenge;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Contracts;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/transfers/*</c> — drei Ja, jederzeit ein Nein.</summary>
/// <remarks>
/// Getrennte Endpunkte statt eines <c>PATCH status</c>: jeder Übergang gehört
/// einer Seite, und ein gemeinsamer müsste bei jedem Aufruf herausfinden, wer
/// gerade was darf. Getrennt steht es in der Adresse.
/// </remarks>
public static class CaseEndpoints
{
    /// <summary>Bildet die neun Routen der Vorgänge ab.</summary>
    public static IEndpointRouteBuilder MapVorgangsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var cases = Responses.Abgesichert(app.MapGroup("/transfers"));

        // Ein Unternehmen zeigt Interesse. Voraussetzung: der MarketStatus ist
        // diesem Unternehmen freigegeben UND die Person ist ansprechbar.
        // `unavailable` heißt nein, auch mit Release — die Release erlaubt zu
        // sehen, nicht zu stören.
        cases.MapPost("/", async (
            InteresseZeigenV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Firma(context, akteur) is not { } company)
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
                new InteresseZeigenBefehl(new SubjectId(body.SubjectId), company, body.Message),
                cancellationToken);

            await Responses.Write(
                context, result, cancellationToken, StatusCodes.Status201Created);
        });

        cases.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Responses.NichtAngemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MyCasesQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Responses.Zu), cancellationToken);
        });

        cases.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Firma(context, akteur) is not { } company)
            {
                return;
            }

            var ihre = await mediator.Send(new CompanyCasesQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                ihre.Select(Responses.Zu), cancellationToken);
        });

        cases.MapPost("/{id:guid}/accept-talk", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.GespraechAnnehmen, mediator, akteur, context,
                cancellationToken));

        cases.MapPost("/{id:guid}/accept-offer", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.AngebotAnnehmen, mediator, akteur, context,
                cancellationToken));

        // Die Person bestätigt, dass ihr Arbeitgeber sie gehen lässt. Die
        // Plattform prüft das nicht und kann es nicht — sie kennt weder den
        // Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage
        // zu stellen und die Antwort festzuhalten.
        cases.MapPost("/{id:guid}/confirm-release", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.FreigabeBestaetigen, mediator, akteur, context,
                cancellationToken));

        // Immer möglich, aus jedem laufenden Zustand. Ein Verfahren, aus dem
        // man nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
        cases.MapPost("/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.Absagen, mediator, akteur, context, cancellationToken));

        cases.MapPost("/{id:guid}/offer", async (
            Guid id,
            AngebotV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Firma(context, akteur) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var result = await mediator.Send(
                new AngebotMachenBefehl(id, company, body.Note, body.StartOn, body.FeeCents),
                cancellationToken);

            await Responses.Write(context, result, cancellationToken);
        })
        // Ein Angebot nennt Eintrittstermin und Gebuehr — es bindet das
        // Unternehmen, und darum verlangt es einen `admin`. Aufgeloest aus der
        // Mitgliedschaftstabelle von identity-service, nie aus dem Token.
        .RequireAuthorization(AdminPermissionExtensions.Richtlinie(CasePermissions.Anbieten));

        cases.MapPost("/{id:guid}/complete", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Firma(id, CompanySwitch.Abschliessen, mediator, akteur, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Richtlinie(CasePermissions.Abschliessen));

        // KEIN Firmenrecht: wer einen Case anfangen darf, muss ihn beenden
        // duerfen. Sonst waere die Einladung eine Falle — derselbe Satz, den
        // `/decline` fuer die Person schon traegt.
        cases.MapPost("/{id:guid}/withdraw", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Firma(id, CompanySwitch.Zurueckziehen, mediator, akteur, context, cancellationToken));

        return app;
    }

    private static async Task Person(
        Guid id,
        Personenzug zug,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (akteur.Current is not { } handelnder)
        {
            await Responses.NichtAngemeldet(context);
            return;
        }

        var result = await mediator.Send(
            new PersonenzugBefehl(id, handelnder.Subject, zug), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }

    private static async Task Firma(
        Guid id,
        CompanySwitch zug,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await Responses.Firma(context, akteur) is not { } company)
        {
            return;
        }

        var result = await mediator.Send(new CompanySwitchCommand(id, company, zug), cancellationToken);

        await Responses.Write(context, result, cancellationToken);
    }
}
