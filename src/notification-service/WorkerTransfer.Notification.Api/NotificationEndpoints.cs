using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Notifications;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Domain.Notifications;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Notification.Api;

/// <summary>The service entrance, the inbox and the switches.</summary>
public static class NotificationEndpoints
{
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
        // Whoever is not on the list gets 404 and not 401: a 401 would confirm
        // that the endpoint exists. For as long as
        // it lay under `/notifications`, the framework took exactly that
        // concealment away again — the path has a gateway route (because of
        // `/notifications/me`), so EVERY method reached the service, and on
        // GET, PUT and DELETE ASP.NET answered 405. And 405 means: this path
        // exists.
        //
        // Under `/internal/` there is no gateway route, as with `/erasure` and
        // `/internal/notify`. All three service-to-service doors then behave
        // alike, and none gives itself away over a method nobody uses.
        var doors = app.MapGroup("/internal");

        doors.MapPost("/notifications", async (
            NotifyV1 body,
            IMediator mediator,
            IMailer mailer,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
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
        }).RequireServiceCaller(
            ServiceClients.Advisor, ServiceClients.Applications, ServiceClients.Assessment,
            ServiceClients.Messages, ServiceClients.Resume, ServiceClients.Scout,
            ServiceClients.Transfer);

        doors.MapServiceFallback();

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
                        [NotificationKind.AssessmentUpdate] = body.AssessmentUpdate,
                        [NotificationKind.MessageReceived] = body.MessageReceived
                    }),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(Response(preference), cancellationToken);
        });

        return app;
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
            preference.Wants(NotificationKind.AssessmentUpdate),
            preference.Wants(NotificationKind.MessageReceived));
}
