using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Notification;
using Noelia.Infrastructure.Security.ServiceTokens;
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
    /// <summary>What comes in: an id, nothing else.</summary>
    private sealed record NotifyBodyV1(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid UserId);

    /// <summary>Maps the one endpoint onto the service's one door group.</summary>
    public static IEndpointRouteBuilder MapNotifyEndpoints(this IEndpointRouteBuilder doors)
    {
        ArgumentNullException.ThrowIfNull(doors);

        doors.MapPost("/notify", async (
            NotifyBodyV1 body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            // The result is discarded. Whether the person exists and whether
            // their account is confirmed is none of the caller's business —
            // otherwise the endpoint would be an oracle about membership of
            // this platform.
            await mediator.Send(
                new ReportNewsCommand(new SubjectId(body.UserId)), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        }).RequireServiceCaller(ServiceClients.Notification);

        return doors;
    }
}
