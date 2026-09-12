using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Consent.Application.Loeschung;
using WorkerTransfer.Consent.Infrastructure.Loeschung;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Api;

/// <summary><c>POST /erasure</c> — the cascade's entrance into this service.</summary>
public static class LoeschEndpoints
{
    /// <summary>The header the origin presents.</summary>
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Maps the one endpoint the erasure cascade calls.</summary>
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
                // The same answer for a wrong secret and for an unset one. A
                // caller must not be able to tell "you guessed wrong" from
                // "this service is not wired up yet".
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var quittung = await mediator.Send(
                new LoeschungAusfuehrenBefehl(body.UserId), cancellationToken);

            // `retained` is a count, not a flag: the origin should learn what
            // stayed rather than guess. Here it is the closing facts the ledger
            // keeps as proof (ADR-0027 §5) — suspended is not skipped.
            await context.Response.WriteAsJsonAsync(
                new LoeschungsquittungV1(quittung.Behalten), cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// Whether this request carries the cascade's secret.
    /// </summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Comparing in fixed time
    /// so the answer's duration says nothing about how much of a guess was
    /// right.
    /// </remarks>
    private static bool DarfLoeschen(HttpContext context, Loescheinstellungen einstellungen)
    {
        if (string.IsNullOrEmpty(einstellungen.Geheimnis))
        {
            return false;
        }

        if (!context.Request.Headers.TryGetValue(Geheimniskopf, out var vorgelegt))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            System.Text.Encoding.UTF8.GetBytes(einstellungen.Geheimnis));
    }
}
