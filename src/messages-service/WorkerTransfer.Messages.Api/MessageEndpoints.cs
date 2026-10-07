using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Conversations;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Contracts;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Messages.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Messages.Api;

/// <summary><c>/messages</c> — one conversation per process (ADR-0047).</summary>
/// <remarks>
/// <para><strong>Six routes, and the set is closed.</strong> Above all there is
/// no route that sends a draft without a human being in front of it, and none
/// that shows a side the other side's drafts (VISION-FRAGEN D2).</para>
///
/// <para><strong><c>member</c> suffices.</strong> Answering an applicant is the
/// daily work in the company's name; it does not bind the company.</para>
/// </remarks>
public static class MessageEndpoints
{
    /// <summary>Maps the routes.</summary>
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // As one filter instead of a catch in every route, because the route
        // that forgets it looks like a working one until something fails.
        var messages = app.MapGroup("/messages").AddEndpointFilter(Intercept);

        messages.MapGet("/me", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var mine = await mediator.Send(new MyConversationsQuery(caller), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                mine.Select(entry => new ConversationSummaryV1(entry.Kind, entry.Reference, entry.LastMessageAt)),
                cancellationToken);
        });

        messages.MapGet("/{kind}/{reference:guid}", async (
            string kind, Guid reference, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var view = await mediator.Send(new ConversationQuery(kind, reference, caller), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new ConversationV1(
                    view.Kind, view.Reference, EfConversationStore.Word(view.Side),
                    [.. view.Messages.Select(message => View(message, view.Side))]),
                cancellationToken);
        });

        messages.MapPost("/{kind}/{reference:guid}", async (
            string kind, Guid reference, WriteMessageV1? body, IMediator mediator,
            ICurrentPrincipal principal, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var message = await mediator.Send(
                new WriteCommand(kind, reference, caller, body?.Text), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(View(message, message.Side), cancellationToken);
        });

        // A model drafts, nobody sends. The text comes back as a draft of the
        // caller's side and waits for a human being (VISION-FRAGEN D2).
        messages.MapPost("/{kind}/{reference:guid}/draft", async (
            string kind, Guid reference, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var text = await mediator.Send(new ComposeReplyQuery(kind, reference, caller), cancellationToken);
            var draft = await mediator.Send(new SaveDraftCommand(kind, reference, caller, text), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(View(draft, draft.Side), cancellationToken);
        });

        messages.MapPost("/drafts/{id:guid}/release", async (
            Guid id, ReleaseDraftV1? body, IMediator mediator, ICurrentPrincipal principal,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var sent = await mediator.Send(new ReleaseDraftCommand(id, caller, body?.Text), cancellationToken);

            await context.Response.WriteAsJsonAsync(View(sent, sent.Side), cancellationToken);
        });

        messages.MapDelete("/drafts/{id:guid}", async (
            Guid id, IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (CallerOf(principal) is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            await mediator.Send(new DiscardDraftCommand(id, caller), cancellationToken);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    /// <summary>The caller, with the company from the verified token — never from a header.</summary>
    private static Caller? CallerOf(ICurrentPrincipal principal) =>
        principal.Current is not { } actor
            ? null
            : new Caller(
                actor.Subject,
                actor.Acting is Capacity.ForCompany company ? company.Tenant : null);

    /// <summary>The one way from a message to the wire.</summary>
    /// <remarks>
    /// The origin only for the own side (ADR-0047, J2), and no author id ever.
    /// </remarks>
    private static MessageV1 View(Message message, Side viewer) =>
        new(message.Id,
            EfConversationStore.Word(message.Side),
            message.Text,
            message.IsDraft ? "draft" : "sent",
            message.Side == viewer,
            message.CreatedAt,
            message.SentAt,
            message.Side == viewer ? EfConversationStore.Word(message.Origin) : null);

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    /// <summary>What a filter returns that has written the response itself — never <c>null</c>.</summary>
    private static readonly IResult AlreadyWritten = Results.Empty;

    private static async ValueTask<object?> Intercept(
        EndpointFilterInvocationContext call, EndpointFilterDelegate next)
    {
        try
        {
            return await next(call);
        }
        catch (NoConversation error)
        {
            // Not yours, no such process, the other side's draft: one answer.
            await Write(call, StatusCodes.Status404NotFound, error.Message);
            return AlreadyWritten;
        }
        catch (InputError error)
        {
            await Write(call, StatusCodes.Status422UnprocessableEntity, error.Message);
            return AlreadyWritten;
        }
        catch (ProcessLookupSilent error)
        {
            // "We do not know" is not "not yours": 503, never 404.
            await Write(call, StatusCodes.Status503ServiceUnavailable, error.Message);
            return AlreadyWritten;
        }
        catch (DraftUnavailable error)
        {
            // The reason's words are what the interface reads ("no draft provider").
            await Write(call, StatusCodes.Status503ServiceUnavailable, error.Message);
            return AlreadyWritten;
        }
    }

    private static Task Write(EndpointFilterInvocationContext call, int status, string detail) =>
        ProblemDetailsMiddleware.Write(call.HttpContext, status, "Request failed", detail);
}
