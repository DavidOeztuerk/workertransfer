using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Transfer.Application.Erasure;
using WorkerTransfer.Transfer.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>POST /erasure</c> — where the cascade enters this service.</summary>
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
                // The same answer for a wrong secret and for an unset one: a
                // caller must not be able to tell "guessed wrong" from
                // "nothing is wired up here yet".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var kept = await mediator.Send(
                new ErasePersonCommand(new Noelia.Core.Identity.SubjectId(body.UserId)),
                cancellationToken);

            // A number, not a flag: by default it is 0, because the paid
            // transfer falls too. If anything ever stayed, the origin should
            // learn of it instead of guessing — suspended is not skipped
            // (ADR-0027 §3.4).
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(kept), cancellationToken);
        });

        return app;
    }

    /// <summary>Whether this request carries the cascade's secret.</summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Compared in fixed time, so
    /// the duration of the answer says nothing about how much of a guess was
    /// right.
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
