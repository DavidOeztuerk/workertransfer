using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Portfolio.Application.Loeschung;
using WorkerTransfer.Portfolio.Infrastructure.Loeschung;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Löschkaskade.</summary>
public static class LoeschEndpoints
{
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bindet den einen Endpunkt ein, den die Kaskade ruft.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpunkt(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            LoeschungV1 koerper,
            IMediator mediator,
            IOptions<Loescheinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfLoeschen(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            // Zeilen UND Dateien. Eine Arbeitsprobe, die nach der Löschung noch
            // auf der Platte liegt, ist genau das stille Scheitern, gegen das
            // ADR-0027 antritt.
            var geblieben = await mediator.Send(
                new PersonLoeschenBefehl(new SubjectId(koerper.UserId)), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new LoeschungsquittungV1(geblieben), cancellationToken);
        });

        return app;
    }

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
