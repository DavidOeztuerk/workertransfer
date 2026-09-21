using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Profile.Application.Erasure;
using WorkerTransfer.Profile.Infrastructure.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Löschkaskade.</summary>
public static class ErasureEndpoints
{
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bindet den einen Endpunkt ein, den die Kaskade ruft.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpunkt(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            ErasureV1 koerper,
            IMediator mediator,
            IOptions<ErasureSettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfLoeschen(context, einstellungen.Value))
            {
                // Dieselbe Antwort für ein falsches und für ein nicht gesetztes
                // Geheimnis: niemand soll „falsch geraten" von „noch nicht
                // verdrahtet" unterscheiden können.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var geblieben = await mediator.Send(
                new ErasePersonCommand(new SubjectId(koerper.UserId)), cancellationToken);

            // Eine Anzahl, kein Schalter: der Ursprung soll erfahren, was blieb,
            // statt es zu vermuten. Hier ist es null — ein PersonProfile kennt keinen
            // Aufbewahrungsschalter.
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(geblieben), cancellationToken);
        });

        return app;
    }

    /// <remarks>
    /// Ein leeres Geheimnis schließt den Endpunkt. In fester Zeit verglichen,
    /// damit die Dauer der Antwort nichts darüber sagt, wie viel eines Rateversuchs
    /// stimmte.
    /// </remarks>
    private static bool DarfLoeschen(HttpContext context, ErasureSettings einstellungen)
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
