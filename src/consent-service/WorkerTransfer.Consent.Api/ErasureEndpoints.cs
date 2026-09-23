using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Consent.Application.Erasure;
using WorkerTransfer.Consent.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Api;

/// <summary><c>POST /erasure</c> — the cascade's entrance into this service.</summary>
public static class ErasureEndpoints
{
    /// <summary>The header the origin presents.</summary>
    private const string SecretHeader = "X-Erasure-Secret";

    /// <summary>Maps the one endpoint the erasure cascade calls.</summary>
    public static IEndpointRouteBuilder MapErasureEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            ErasureV1 body,
            IMediator mediator,
            IOptions<ErasureSettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayErase(context, settings.Value))
            {
                // The same answer for a wrong secret and for an unset one. A
                // caller must not be able to tell "you guessed wrong" from
                // "this service is not wired up yet".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var receipt = await mediator.Send(
                new ExecuteErasureCommand(body.UserId), cancellationToken);

            // `retained` is a count, not a flag: the origin should learn what
            // stayed rather than guess. Here it is the closing facts the ledger
            // keeps as proof (ADR-0027 §5) — suspended is not skipped.
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(receipt.Retained), cancellationToken);
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
    private static bool MayErase(HttpContext context, ErasureSettings settings)
    {
        if (string.IsNullOrEmpty(settings.Geheimnis))
        {
            return false;
        }

        if (!context.Request.Headers.TryGetValue(SecretHeader, out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented.ToString()),
            System.Text.Encoding.UTF8.GetBytes(settings.Geheimnis));
    }
}
