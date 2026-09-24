using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Scout.Application.Erasure;
using WorkerTransfer.Scout.Infrastructure.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Api;

/// <summary><c>POST /erasure</c> — the entrance of the erasure cascade.</summary>
/// <remarks>
/// This service has been an erasure recipient since it has had its first table
/// (ADR-0027 §4, ADR-0036): <c>searches</c> carries a <c>subject_id</c>, the
/// outbox a <c>user_id</c>. <c>"scout"</c> therefore stands in
/// <c>ErasureRecipient.Fremde</c> and in <c>LoeschempfaengerTests.Dienste</c>.
/// </remarks>
public static class ErasureEndpoints
{
    /// <summary>The header the origin presents.</summary>
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var kept = await mediator.Send(
                new ErasePersonCommand(new SubjectId(body.UserId)), cancellationToken);

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
