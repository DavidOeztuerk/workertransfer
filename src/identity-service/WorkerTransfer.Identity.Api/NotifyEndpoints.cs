using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Application.Notification;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary><c>POST /internal/notify</c> — the only way to an address.</summary>
/// <remarks>
/// The Python service kept the notifications here, because "a
/// <c>notifications-service</c> would need the email address". The objection is
/// right; it is answered not by ignoring it but by moving the <em>other</em>
/// half. At notification-service lives <strong>whether</strong> anything goes
/// out — the four switches and the throttle. Here lives <strong>to
/// whom</strong>. What crosses the boundary is nothing but a <c>userId</c>,
/// which travels everywhere else already.
/// <para>
/// That is why the body carries no kind and no text either: the sentence comes
/// into being here, always the same one, and the caller can contribute nothing.
/// </para>
/// </remarks>
public static class NotifyEndpoints
{
    /// <summary>The header notification-service sends along.</summary>
    private const string SecretHeader = "X-Notify-Secret";

    /// <summary>What comes in: an id, nothing else.</summary>
    private sealed record NotifyBodyV1(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid UserId);

    /// <summary>Maps the one endpoint.</summary>
    public static IEndpointRouteBuilder MapNotifyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/internal/notify", async (
            NotifyBodyV1 body,
            IMediator mediator,
            IOptions<NotifySettings> settings,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!MayNotify(context, settings.Value))
            {
                // 404 instead of 401: a 401 confirms the endpoint exists.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            // The result is discarded. Whether the person exists and whether
            // their account is confirmed is none of the caller's business —
            // otherwise the endpoint would be an oracle about membership of
            // this platform.
            await mediator.Send(
                new ReportNewsCommand(new SubjectId(body.UserId)), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        return app;
    }

    /// <summary>Whether this request carries the secret.</summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Compared in fixed time so
    /// the answer's duration says nothing about how much of a guess was right.
    /// </remarks>
    private static bool MayNotify(HttpContext context, NotifySettings settings)
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
