using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Benachrichtigungen;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;
using WorkerTransfer.Notification.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Api;

/// <summary>Der Diensteingang, das Postfach und die vier Schalter.</summary>
public static class BenachrichtigungsEndpoints
{
    /// <summary>Der Kopf, den ein Dienst mitschickt.</summary>
    /// <remarks>
    /// Ein Browser kann ihn nicht setzen, ohne das Geheimnis zu kennen, und er
    /// kennt es nicht.
    /// </remarks>
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Bildet die fünf Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapBenachrichtigungsEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Immer 202 — auch wenn nichts hinausging. Abbestellt, gedrosselt,
        // Person unbekannt: der Aufrufer erfährt nichts darüber, ob und warum.
        // Sonst wäre der Endpunkt ein Orakel darüber, ob es diese Person gibt
        // und ob sie Post will.
        // `/internal/notifications` und nicht `/notifications`.
        //
        // Der Dienst antwortet ohne das gemeinsame Geheimnis bewusst mit 404
        // und nicht 401: ein 401 bestaetigte, dass es den Endpunkt gibt. Solange
        // er unter `/notifications` lag, nahm ihm das Rahmenwerk genau diese
        // Verschleierung wieder ab — der Pfad hat eine Gateway-Route (wegen
        // `/notifications/me`), also erreichte JEDE Methode den Dienst, und auf
        // GET, PUT, DELETE antwortete ASP.NET mit 405. Und 405 heisst: diesen
        // Pfad gibt es.
        //
        // Unter `/internal/` gibt es keine Gateway-Route, wie bei `/erasure`
        // und `/internal/notify`. Damit verhalten sich alle drei
        // Dienst-zu-Dienst-Tueren gleich, und keine verraet sich ueber eine
        // Methode, die niemand benutzt.
        app.MapPost("/internal/notifications", async (
            BenachrichtigenV1 body,
            IMediator mediator,
            IPostbote postbote,
            IOptions<Posteinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                // 404 statt 401: ein 401 bestätigt, dass es den Endpunkt gibt.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            if (Benachrichtigungsarten.Lies(body.Kind) is not { } art)
            {
                // Eine Art, die es nicht gibt, ist ein Fehler des Aufrufers und
                // keine Aussage über die Person — die einzige Stelle, an der
                // dieser Endpunkt etwas anderes als 202 antwortet.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "That is not a notification kind");
                return;
            }

            var wer = new SubjectId(body.UserId);
            var gesendet = await mediator.Send(
                new BenachrichtigenBefehl(wer, art), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status202Accepted;

            if (gesendet)
            {
                // NACH dem Befehl und damit nach dem Commit: vorher zu bitten
                // hieße, über etwas zu benachrichtigen, das gleich
                // zurückgerollt wird. Der Postbote wirft nicht.
                await postbote.SchickeAsync(wer, cancellationToken);
            }
        });

        var meine = app.MapGroup("/notifications/me");

        meine.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            // Wessen Postfach, steht im geprüften Token. Es gibt keine Route
            // mit einer Kennung im Pfad: die wäre ein Orakel darüber, ob und
            // wie oft jemandem etwas passiert.
            var eingaenge = await mediator.Send(
                new MeinPostfachAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                eingaenge.Select(Antwort), cancellationToken);
        });

        meine.MapPost("/read", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var gelesen = await mediator.Send(
                new AllesGelesenBefehl(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(new { read = gelesen }, cancellationToken);
        });

        app.MapGet("/me/notification-preferences", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var wunsch = await mediator.Send(
                new MeineWuenscheAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Antwort(wunsch), cancellationToken);
        });

        app.MapPut("/me/notification-preferences", async (
            BenachrichtigungswuenscheV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var wunsch = await mediator.Send(
                new WuenscheSchreibenBefehl(
                    handelnder.Subject,
                    new Dictionary<Benachrichtigungsart, bool>
                    {
                        [Benachrichtigungsart.ResumeRequest] = body.ResumeRequest,
                        [Benachrichtigungsart.MarketRequest] = body.MarketRequest,
                        [Benachrichtigungsart.ApplicationUpdate] = body.ApplicationUpdate,
                        [Benachrichtigungsart.TransferUpdate] = body.TransferUpdate,
                        [Benachrichtigungsart.ApplicationReceived] = body.ApplicationReceived,
                        [Benachrichtigungsart.ProfileDiscovered] = body.ProfileDiscovered,
                        [Benachrichtigungsart.AdvisorConversation] = body.AdvisorConversation
                    }),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(Antwort(wunsch), cancellationToken);
        });

        return app;
    }

    /// <summary>Ob diese Anfrage das Geheimnis der Dienste trägt.</summary>
    /// <remarks>
    /// Ein leeres eingestelltes Geheimnis schließt den Endpunkt. In fester Zeit
    /// verglichen, damit die Dauer der Antwort nichts darüber sagt, wie viel
    /// von einem Rateversuch stimmte.
    /// </remarks>
    private static bool DarfMelden(HttpContext context, Posteinstellungen einstellungen)
    {
        if (string.IsNullOrEmpty(einstellungen.Geheimnis)
            || !context.Request.Headers.TryGetValue(Geheimniskopf, out var vorgelegt))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            System.Text.Encoding.UTF8.GetBytes(einstellungen.Geheimnis));
    }

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static EingangV1 Antwort(Eingang eingang) =>
        new(eingang.Id,
            Benachrichtigungsarten.Wort(eingang.Art),
            eingang.AngelegtAm,
            eingang.GelesenAm);

    private static BenachrichtigungswuenscheV1 Antwort(Benachrichtigungswunsch wunsch) =>
        new(wunsch.Will(Benachrichtigungsart.ResumeRequest),
            wunsch.Will(Benachrichtigungsart.MarketRequest),
            wunsch.Will(Benachrichtigungsart.ApplicationUpdate),
            wunsch.Will(Benachrichtigungsart.TransferUpdate),
            wunsch.Will(Benachrichtigungsart.ApplicationReceived),
            wunsch.Will(Benachrichtigungsart.ProfileDiscovered),
            wunsch.Will(Benachrichtigungsart.AdvisorConversation));
}
