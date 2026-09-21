using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Notifications;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Domain.Notifications;
using WorkerTransfer.Notification.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Api;

/// <summary>The service entrance, the inbox and the switches.</summary>
public static class NotificationEndpoints
{
    /// <summary>The header a service sends along.</summary>
    /// <remarks>
    /// A browser cannot set it without knowing the secret, and it does not know
    /// it.
    /// </remarks>
    private const string SecretHeader = "X-Notify-Secret";

    /// <summary>Maps the five routes of this service.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Always 202 — even when nothing went out. Switched off, throttled,
        // person unknown: the caller learns nothing about whether or why.
        // Otherwise the endpoint would be an oracle about whether this person
        // exists and whether they want mail.
        // `/internal/notifications` and not `/notifications`.
        //
        // Without the shared secret the service deliberately answers 404 and
        // not 401: a 401 would confirm that the endpoint exists. For as long as
        // it lay under `/notifications`, the framework took exactly that
        // concealment away again — the path has a gateway route (because of
        // `/notifications/me`), so EVERY method reached the service, and on
        // GET, PUT and DELETE ASP.NET answered 405. And 405 means: this path
        // exists.
        //
        // Under `/internal/` there is no gateway route, as with `/erasure` and
        // `/internal/notify`. All three service-to-service doors then behave
        // alike, and none gives itself away over a method nobody uses.
        app.MapPost("/internal/notifications", async (
            NotifyV1 body,
            IMediator mediator,
            IMailer mailer,
            IOptions<MailSettings> settings,
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

            if (NotificationKinds.Read(body.Kind) is not { } kind)
            {
                // A kind that does not exist is a fault of the caller and no
                // statement about the person — the one place where this
                // endpoint answers anything other than 202.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "That is not a notification kind");
                return;
            }

            var subject = new SubjectId(body.UserId);
            var sent = await mediator.Send(
                new NotifyCommand(subject, kind), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status202Accepted;

            if (sent)
            {
                // AFTER the command and therefore after the commit: asking
                // earlier would mean notifying about something that is about to
                // be rolled back. The mailer does not throw.
                await mailer.SendAsync(subject, cancellationToken);
            }
        });

        var mine = app.MapGroup("/notifications/me");

        mine.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            // Whose inbox stands in the verified token. There is no route with
            // an id in the path: that would be an oracle about whether and how
            // often something happens to somebody.
            var inbox = await mediator.Send(
                new MyInboxQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                inbox.Select(Response), cancellationToken);
        });

        mine.MapPost("/read", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            var read = await mediator.Send(
                new ReadAllCommand(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(new { read = read }, cancellationToken);
        });

        app.MapGet("/me/notification-preferences", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            var preference = await mediator.Send(
                new MyPreferencesQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Response(preference), cancellationToken);
        });

        app.MapPut("/me/notification-preferences", async (
            NotificationPreferencesV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var preference = await mediator.Send(
                new WritePreferencesCommand(
                    actor.Subject,
                    new Dictionary<NotificationKind, bool>
                    {
                        [NotificationKind.ResumeRequest] = body.ResumeRequest,
                        [NotificationKind.MarketRequest] = body.MarketRequest,
                        [NotificationKind.ApplicationUpdate] = body.ApplicationUpdate,
                        [NotificationKind.TransferUpdate] = body.TransferUpdate,
                        [NotificationKind.ApplicationReceived] = body.ApplicationReceived,
                        [NotificationKind.ProfileDiscovered] = body.ProfileDiscovered,
                        [NotificationKind.AdvisorConversation] = body.AdvisorConversation,
                        [NotificationKind.AssessmentUpdate] = body.AssessmentUpdate
                    }),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(Response(preference), cancellationToken);
        });

        return app;
    }

    /// <summary>Whether this request carries the services' secret.</summary>
    /// <remarks>
    /// An empty configured secret shuts the endpoint. Compared in fixed time,
    /// so the duration of the answer says nothing about how much of a guess
    /// was right.
    /// </remarks>
    private static bool MayNotify(HttpContext context, MailSettings settings)
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

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static InboxEntryV1 Response(InboxEntry entry) =>
        new(entry.Id,
            NotificationKinds.Word(entry.Kind),
            entry.CreatedAt,
            entry.ReadAt);

    private static NotificationPreferencesV1 Response(NotificationPreference preference) =>
        new(preference.Wants(NotificationKind.ResumeRequest),
            preference.Wants(NotificationKind.MarketRequest),
            preference.Wants(NotificationKind.ApplicationUpdate),
            preference.Wants(NotificationKind.TransferUpdate),
            preference.Wants(NotificationKind.ApplicationReceived),
            preference.Wants(NotificationKind.ProfileDiscovered),
            preference.Wants(NotificationKind.AdvisorConversation),
            preference.Wants(NotificationKind.AssessmentUpdate));
}
