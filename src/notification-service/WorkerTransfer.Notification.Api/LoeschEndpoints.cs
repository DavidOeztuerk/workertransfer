using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Loeschung;
using WorkerTransfer.Notification.Infrastructure.Loeschung;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Kaskade in diesen Dienst.</summary>
public static class LoeschEndpoints
{
    /// <summary>Der Kopf, den der Ursprung vorlegt.</summary>
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bildet den einen Endpunkt ab, den die Löschkaskade ruft.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            LoeschungV1 body,
            IMediator mediator,
            IOptions<Loescheinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfLoeschen(context, einstellungen.Value))
            {
                // Dieselbe Antwort für ein falsches und für ein nicht gesetztes
                // Geheimnis: ein Aufrufer darf „falsch geraten" nicht von „hier
                // ist noch nichts verdrahtet" unterscheiden können.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var behalten = await mediator.Send(
                new PersonLoeschenBefehl(new Girder.Core.Identity.SubjectId(body.UserId)),
                cancellationToken);

            // Eine Zahl, kein Merker — hier immer 0, weil es nichts gibt, was
            // stehen bliebe. Die Form ist trotzdem dieselbe wie überall: der
            // Ursprung soll die Antwort lesen können, ohne zu wissen, welcher
            // Dienst gerade geantwortet hat.
            await context.Response.WriteAsJsonAsync(
                new LoeschungsquittungV1(behalten), cancellationToken);
        });

        return app;
    }

    /// <summary>Ob diese Anfrage das Geheimnis der Kaskade trägt.</summary>
    /// <remarks>
    /// Ein leeres eingestelltes Geheimnis schließt den Endpunkt. In fester Zeit
    /// verglichen, damit die Dauer der Antwort nichts darüber sagt, wie viel
    /// von einem Rateversuch stimmte.
    /// </remarks>
    private static bool DarfLoeschen(HttpContext context, Loescheinstellungen einstellungen)
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
}
