using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Erasure;
using WorkerTransfer.Applications.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

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

            // A count, not a flag: by default it is 0, because the hired
            // application falls too. Should anything ever stay, the origin is to
            // learn it rather than guess — suspended is not skipped
            // (ADR-0027 §3.4).
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
        if (string.IsNullOrEmpty(settings.Secret)
            || !context.Request.Headers.TryGetValue(SecretHeader, out var presented))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(presented.ToString()),
            System.Text.Encoding.UTF8.GetBytes(settings.Secret));
    }
}
