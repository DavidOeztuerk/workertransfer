using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Anfragen;
using WorkerTransfer.Transfer.Application.Markt;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Contracts;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/market/*</c> — der Status, seine Release und ihr Revocation.</summary>
/// <remarks>
/// Hier gibt es kein <c>:public</c>, und es darf keines geben. Beim Profile ist
/// „für alle Unternehmen" eine sinnvolle Wahl; hier wäre sie ein Schalter,
/// dessen Folgen niemand überblickt — darunter der eigene Arbeitgeber, der auf
/// derselben Plattform ist.
/// </remarks>
public static class MarketEndpoints
{
    /// <summary>Bildet die acht Routen des Marktes ab.</summary>
    public static IEndpointRouteBuilder MapMarktEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var market = Responses.Abgesichert(app.MapGroup("/market"));

        market.MapPut("/me", async (
            WriteMarketStatusV1 body,
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

            ArgumentNullException.ThrowIfNull(body);

            if (Verfuegbarkeiten.Lies(body.Availability) is not { } verfuegbarkeit)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "That is not an availability");
                return;
            }

            try
            {
                // Wessen Status es ist, kommt aus dem geprüften Token und nie
                // aus dem Rumpf: eine Subjekt-Kennung auf der Leitung wäre ein
                // Weg, den MarketStatus eines anderen zu setzen.
                var status = await mediator.Send(
                    new SaveMarketStatusCommand(
                        handelnder.Subject, verfuegbarkeit, body.Employed, body.Note),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    Responses.Zu(status), cancellationToken);
            }
            catch (MarketError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        // Nie `null`: „nichts gesagt" IST ein Zustand, nämlich `unavailable`.
        // Ein `null` würde die Oberfläche zwingen, sich eine Voreinstellung
        // auszudenken, und die Gefahr ist, dass sie sich die falsche ausdenkt.
        market.MapGet("/me", async (
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

            var status = await mediator.Send(
                new MyMarketStatusQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Responses.Zu(status), cancellationToken);
        });

        // Wer hat gefragt — und was gerade gilt. `active` kommt frisch aus dem
        // Ledger und kann vom State abweichen: nach einem Revocation bleibt
        // GRANTED stehen, `active` fällt auf false.
        market.MapGet("/me/requests", async (
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
                new MyRequestsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(ansicht => Responses.Zu(ansicht.ResumeRequest, ansicht.Aktiv)),
                cancellationToken);
        });

        // Auch abgelehnte bleiben sichtbar. Sonst sähen „abgelehnt" und „nie
        // gefragt" gleich aus — und dann fragt jemand erneut, im guten Glauben.
        market.MapGet("/requests", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Firma(context, akteur) is not { } company)
            {
                return;
            }

            var requests = await mediator.Send(
                new CompanyRequestsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                requests.Select(request => Responses.Zu(request, aktiv: null)),
                cancellationToken);
        });

        market.MapPost("/requests/{id:guid}/grant", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: true, mediator, akteur, context, cancellationToken));

        market.MapPost("/requests/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: false, mediator, akteur, context, cancellationToken));

        // Der Revocation wirkt im Ledger, nicht im Case. Ein laufender
        // Transfer bleibt bestehen: er hat seine eigene Tür und seine eigene
        // Absage.
        market.MapPost("/requests/{id:guid}/revoke", async (
            Guid id,
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

            var result = await mediator.Send(
                new RevokeAccessCommand(id, handelnder.Subject), cancellationToken);

            await Responses.Write(context, result, cancellationToken, aktiv: false);
        });

        // „Darf ich sehen, ob du gerade zuhörst?" — die leichtere der beiden
        // Fragen. Die schwerere ist der Case selbst.
        market.MapPost("/{subjectId:guid}/requests", async (
            Guid subjectId,
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

            if (await Responses.Firma(context, akteur) is not { } company)
            {
                return;
            }

            var result = await mediator.Send(
                new RequestMarketStatusCommand(
                    new SubjectId(subjectId), company, handelnder.Subject),
                cancellationToken);

            // Das anfragende Unternehmen bekommt kein `active`: es hat die
            // Antwort schon in Form des Status, den es sieht oder nicht sieht.
            await Responses.Write(
                context, result, cancellationToken,
                aktiv: null, erfolg: StatusCodes.Status201Created);
        });

        market.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Responses.Firma(context, akteur) is not { } company)
            {
                return;
            }

            var status = await mediator.Send(
                new VisibleMarketStatusQuery(new SubjectId(subjectId), company),
                cancellationToken);

            if (status is null)
            {
                // Nicht vorhanden, nicht freigegeben — dieselbe Antwort. Der
                // Unterschied wäre hier besonders teuer: schon die Existenz der
                // Aussage verrät etwas.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;
            }

            await context.Response.WriteAsJsonAsync(Responses.Zu(status), cancellationToken);
        });

        return app;
    }

    private static async Task Beantworte(
        Guid id,
        bool erteilen,
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
            new AnswerRequestCommand(id, handelnder.Subject, erteilen), cancellationToken);

        await Responses.Write(context, result, cancellationToken, aktiv: erteilen);
    }
}
