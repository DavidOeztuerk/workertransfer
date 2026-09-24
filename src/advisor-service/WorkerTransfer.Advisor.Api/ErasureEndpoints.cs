using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Erasure;
using WorkerTransfer.Advisor.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Api;

/// <summary><c>POST /erasure</c> — the entrance of the erasure cascade.</summary>
/// <remarks>
/// This service has been an erasure recipient since it got its first table
/// (ADR-0027 §4, ADR-0037): <c>mandates</c> carries the person as its key,
/// <c>conversations</c> a <c>subject_id</c>, the outbox a <c>user_id</c>.
/// <c>"advisor"</c> therefore stands in <c>ErasureRecipient.Services</c> and in
/// <c>ErasureRecipientTests.Services</c>.
/// </remarks>
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
