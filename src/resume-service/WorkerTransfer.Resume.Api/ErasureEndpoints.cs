using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Resume.Application.Erasure;
using WorkerTransfer.Resume.Infrastructure.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Api;

/// <summary><c>POST /erasure</c> — the cascade's entrance into this service.</summary>
public static class ErasureEndpoints
{
    /// <summary>The header the origin presents.</summary>
    private const string SecretHeader = "X-Erasure-Secret";

    /// <summary>Maps the one endpoint the erasure cascade calls.</summary>
    public static IEndpointRouteBuilder MapErasureEndpoint(this IEndpointRouteBuilder app)
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
                // The same answer for a wrong secret and for an unset one: a
                // caller must not be able to tell "you guessed wrong" from
                // "this service is not wired up yet".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var kept = await mediator.Send(
                new ErasePersonCommand(new SubjectId(body.UserId)), cancellationToken);

            // A count, not a flag: the origin should learn what stayed rather
            // than guess. Here it is zero — a résumé names real employers with
            // dates, and there is no retention switch that would keep one.
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(kept), cancellationToken);
        });

        return app;
    }

    /// <summary>Whether this request carries the cascade's secret.</summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Compared in fixed time so
    /// the answer's duration says nothing about how much of a guess was right.
    /// </remarks>
    private static bool MayErase(HttpContext context, ErasureSettings settings)
    {
        if (string.IsNullOrEmpty(settings.Geheimnis)
            || !context.Request.Headers.TryGetValue(SecretHeader, out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented.ToString()),
            System.Text.Encoding.UTF8.GetBytes(settings.Geheimnis));
    }
}
