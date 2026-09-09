using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Application.Anfragen;
using WorkerTransfer.Transfer.Application.Markt;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Contracts;
using WorkerTransfer.Transfer.Domain.Anfragen;
using WorkerTransfer.Transfer.Domain.Markt;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/market/*</c> — der Status, seine Freigabe und ihr Widerruf.</summary>
/// <remarks>
/// Hier gibt es kein <c>:public</c>, und es darf keines geben. Beim Profil ist
/// „für alle Unternehmen" eine sinnvolle Wahl; hier wäre sie ein Schalter,
/// dessen Folgen niemand überblickt — darunter der eigene Arbeitgeber, der auf
/// derselben Plattform ist.
/// </remarks>
public static class MarktEndpoints
{
    /// <summary>Bildet die acht Routen des Marktes ab.</summary>
    public static IEndpointRouteBuilder MapMarktEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var markt = Antworten.Abgesichert(app.MapGroup("/market"));

        markt.MapPut("/me", async (
            MarktstatusSchreibenV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            if (Verfuegbarkeiten.Lies(body.Availability) is not { } verfuegbarkeit)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "That is not an availability");
                return;
            }

            try
            {
                // Wessen Status es ist, kommt aus dem geprüften Token und nie
                // aus dem Rumpf: eine Subjekt-Kennung auf der Leitung wäre ein
                // Weg, den Marktstatus eines anderen zu setzen.
                var status = await mediator.Send(
                    new MarktstatusSichernBefehl(
                        handelnder.Subject, verfuegbarkeit, body.Employed, body.Note),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    Antworten.Zu(status), cancellationToken);
            }
            catch (Marktfehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        // Nie `null`: „nichts gesagt" IST ein Zustand, nämlich `unavailable`.
        // Ein `null` würde die Oberfläche zwingen, sich eine Voreinstellung
        // auszudenken, und die Gefahr ist, dass sie sich die falsche ausdenkt.
        markt.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            var status = await mediator.Send(
                new MeinMarktstatusAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Antworten.Zu(status), cancellationToken);
        });

        // Wer hat gefragt — und was gerade gilt. `active` kommt frisch aus dem
        // Ledger und kann vom Stand abweichen: nach einem Widerruf bleibt
        // GRANTED stehen, `active` fällt auf false.
        markt.MapGet("/me/requests", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MeineAnfragenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(ansicht => Antworten.Zu(ansicht.Anfrage, ansicht.Aktiv)),
                cancellationToken);
        });

        // Auch abgelehnte bleiben sichtbar. Sonst sähen „abgelehnt" und „nie
        // gefragt" gleich aus — und dann fragt jemand erneut, im guten Glauben.
        markt.MapGet("/requests", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var anfragen = await mediator.Send(
                new FirmenanfragenAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                anfragen.Select(anfrage => Antworten.Zu(anfrage, aktiv: null)),
                cancellationToken);
        });

        markt.MapPost("/requests/{id:guid}/grant", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: true, mediator, akteur, context, cancellationToken));

        markt.MapPost("/requests/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: false, mediator, akteur, context, cancellationToken));

        // Der Widerruf wirkt im Ledger, nicht im Vorgang. Ein laufender
        // Transfer bleibt bestehen: er hat seine eigene Tür und seine eigene
        // Absage.
        markt.MapPost("/requests/{id:guid}/revoke", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            var ergebnis = await mediator.Send(
                new ZugriffWiderrufenBefehl(id, handelnder.Subject), cancellationToken);

            await Antworten.Schreibe(context, ergebnis, cancellationToken, aktiv: false);
        });

        // „Darf ich sehen, ob du gerade zuhörst?" — die leichtere der beiden
        // Fragen. Die schwerere ist der Vorgang selbst.
        markt.MapPost("/{subjectId:guid}/requests", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var ergebnis = await mediator.Send(
                new MarktstatusAnfragenBefehl(
                    new SubjectId(subjectId), firma, handelnder.Subject),
                cancellationToken);

            // Das anfragende Unternehmen bekommt kein `active`: es hat die
            // Antwort schon in Form des Status, den es sieht oder nicht sieht.
            await Antworten.Schreibe(
                context, ergebnis, cancellationToken,
                aktiv: null, erfolg: StatusCodes.Status201Created);
        });

        markt.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var status = await mediator.Send(
                new SichtbarerMarktstatusAbfrage(new SubjectId(subjectId), firma),
                cancellationToken);

            if (status is null)
            {
                // Nicht vorhanden, nicht freigegeben — dieselbe Antwort. Der
                // Unterschied wäre hier besonders teuer: schon die Existenz der
                // Aussage verrät etwas.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such market status");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antworten.Zu(status), cancellationToken);
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
            await Antworten.NichtAngemeldet(context);
            return;
        }

        var ergebnis = await mediator.Send(
            new AnfrageBeantwortenBefehl(id, handelnder.Subject, erteilen), cancellationToken);

        await Antworten.Schreibe(context, ergebnis, cancellationToken, aktiv: erteilen);
    }
}
