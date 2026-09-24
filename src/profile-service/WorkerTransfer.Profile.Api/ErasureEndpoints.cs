using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Profile.Application.Erasure;
using WorkerTransfer.Profile.Infrastructure.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary><c>POST /erasure</c> — the entrance of the erasure cascade.</summary>
public static class ErasureEndpoints
{
    private const string SecretHeader = "X-Erasure-Secret";

    /// <summary>Maps the one endpoint the cascade calls.</summary>
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
                // The same answer for a wrong and for an unset secret: nobody is
                // to tell "guessed wrong" from "not wired yet".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var kept = await mediator.Send(
                new ErasePersonCommand(new SubjectId(body.UserId)), cancellationToken);

            // A number, not a switch: the origin is to learn what remained
            // instead of guessing it. Here it is zero — a profile knows no
            // retention switch.
            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(kept), cancellationToken);
        });

        return app;
    }

    /// <remarks>
    /// An empty secret shuts the endpoint. Compared in fixed time, so that the
    /// duration of the answer says nothing about how much of a guess was
    /// right.
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
