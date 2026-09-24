using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Erasure;
using WorkerTransfer.Notification.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Api;

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

            // A number, not a flag — always 0 here, because there is nothing
            // that would stay. The shape is the same as everywhere all the
            // same: the origin should be able to read the answer without
            // knowing which service just answered.
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(kept), cancellationToken);
        });

        return app;
    }

    /// <summary>Whether this request carries the cascade's secret.</summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Compared in fixed time,
    /// so the duration of the answer says nothing about how much of a guess
    /// was right.
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
