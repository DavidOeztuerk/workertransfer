using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Drafts;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary><c>/applications/drafts/*</c> — write, review, approve, send.</summary>
/// <remarks>
/// <strong>There is no way from "generating" to "sent" without two clicks of a
/// human being</strong> (ADR-0034). The bulk action lies at creating, where it
/// does no harm; it does not stand at the exit.
/// </remarks>
public static class DraftEndpoints
{
    private static readonly IResult Written = Results.Empty;

    /// <summary>Maps the draft routes.</summary>
    public static IEndpointRouteBuilder MapDraftEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var drafts = app.MapGroup("/applications/drafts");

        // Three silent dependencies, one status code: neither the ledger nor
        // jobs-service nor a service holding one's own details may turn into a
        // statement about the application.
        drafts.AddEndpointFilter(async (call, next) =>
        {
            try
            {
                return await next(call);
            }
            catch (ConsentGateSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Written;
            }
            catch (JobLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "jobs-service did not answer");

                return Written;
            }
            catch (ApplicantLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a service holding your own data did not answer");

                return Written;
            }
            catch (CompanyLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "identity-service did not answer");

                return Written;
            }
        });

        drafts.MapPost("/", async (
            CreateDraftsV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            // WHOEVER ASKS FOR NOTHING GETS NO 201.
            //
            // An empty list created zero drafts and still reported "Created" — a
            // status code that claims something that did not happen. Measured at
            // the route map, which recorded 422 as the intent; the map was right.
            //
            // NOT affected: a list with ids that do not exist (any more). A
            // closed advert is no faulty request, and the answer is then a
            // shorter list.
            if (body?.JobIds is not { Count: > 0 } jobs)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: job_ids");
                return;
            }

            var created = await mediator.Send(
                new CreateDraftsCommand(actor.Subject, jobs),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                created.Select(ToContract).ToArray(), cancellationToken);
        });

        drafts.MapPost("/{id:guid}/write", Step(
            (id, subject) => new WriteDraftCommand(subject, id),
            CoverLetterJobKind.Write));

        drafts.MapPost("/{id:guid}/revise", Step(
            (id, subject) => new ReviseCommand(subject, id),
            CoverLetterJobKind.Revise));

        drafts.MapPost("/{id:guid}/approve", Step(
            (id, subject) => new ApproveDraftCommand(subject, id)));

        drafts.MapPost("/{id:guid}/send", Step(
            (id, subject) => new SendDraftCommand(subject, id)));

        drafts.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            ICoverLetterQueue queue,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            var mine = await mediator.Send(
                new MyDraftsQuery(actor.Subject), cancellationToken);

            foreach (var open in mine)
            {
                StartIfGenerating(open, actor.Subject, context, queue);
            }

            await context.Response.WriteAsJsonAsync(
                mine.Select(ToContract).ToArray(), cancellationToken);
        });

        drafts.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            ICoverLetterQueue queue,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            var draft = await mediator.Send(
                new DraftQuery(actor.Subject, id), cancellationToken);

            if (draft is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;
            }

            StartIfGenerating(draft, actor.Subject, context, queue);

            await context.Response.WriteAsJsonAsync(ToContract(draft), cancellationToken);
        });

        drafts.MapPatch("/{id:guid}", async (
            Guid id,
            ChangeDraftV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            await Answer(
                context,
                await mediator.Send(
                    new ChangeDraftCommand(
                        actor.Subject, id, body?.Subject, body?.Body),
                    cancellationToken),
                cancellationToken);
        });

        drafts.MapPut("/{id:guid}/attachments", async (
            Guid id,
            AttachmentsV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            await Answer(
                context,
                await mediator.Send(
                    new ChooseAttachmentsCommand(
                        actor.Subject, id,
                        body?.SharesResume ?? true,
                        body?.Documents ?? []),
                    cancellationToken),
                cancellationToken);
        });

        drafts.MapPost("/{id:guid}/comments", async (
            Guid id,
            AddCommentV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            await Answer(
                context,
                await mediator.Send(
                    new AddCommentCommand(actor.Subject, id, body?.Text, body?.Quote),
                    cancellationToken),
                cancellationToken);
        });

        drafts.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            var gone = await mediator.Send(
                new DeleteDraftCommand(actor.Subject, id), cancellationToken);

            context.Response.StatusCode = gone
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        return app;
    }

    /// <summary>A step without a body — always the same course.</summary>
    /// <remarks>
    /// Four routes differ only in the command. Writing them out one by one would
    /// be four chances to forget the sign-in check or an error mapping.
    /// </remarks>
    private static Delegate Step(
        Func<Guid, SubjectId, IRequest<DraftResult>> build,
        CoverLetterJobKind? afterwards = null) =>
        async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal principal,
            ICoverLetterQueue queue,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotSignedIn(context);
                return;
            }

            var result = await mediator.Send(
                build(id, actor.Subject), cancellationToken);

            await Answer(context, result, cancellationToken);

            if (afterwards is { } kind && result is DraftResult.Done)
            {
                queue.Enqueue(new CoverLetterJob(
                    actor.Subject, id, Bearer(context), kind));
            }
        };

    /// <summary>
    /// The worker lives in the process. After a restart the queue is empty, but
    /// the draft still stands at Generating — then nobody writes, and the
    /// interface polls an empty letter. A GET suffices to queue the same job
    /// again; if it is already running, Enqueue declines.
    /// </summary>
    private static void StartIfGenerating(
        ApplicationDraft draft,
        SubjectId subject,
        HttpContext context,
        ICoverLetterQueue queue)
    {
        if (draft.State != DraftState.Generating)
        {
            return;
        }

        if (queue.IsRunning(draft.Id))
        {
            return;
        }

        queue.Enqueue(new CoverLetterJob(
            subject,
            draft.Id,
            Bearer(context),
            draft.HasOpenComments
                ? CoverLetterJobKind.Revise
                : CoverLetterJobKind.Write));
    }

    private static string? Bearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..];
        }

        return context.Request.Cookies.TryGetValue("access", out var fromCookie)
               && !string.IsNullOrEmpty(fromCookie)
            ? fromCookie
            : null;
    }

    private static async Task Answer(
        HttpContext context, DraftResult result, CancellationToken cancellationToken)
    {
        switch (result)
        {
            case DraftResult.Done done:
                await context.Response.WriteAsJsonAsync(
                    ToContract(done.Draft), cancellationToken);
                return;

            case DraftResult.None:
                // "Does not exist" and "belongs to someone else" are the same
                // from outside — otherwise a 403 would give the existence away.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;

            case DraftResult.NoProvider none:
                // 503 and not 500: nothing is broken, nothing is set up — and the
                // interface tells the human being so instead of silently doing
                // nothing (ADR-0024).
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", none.Reason);
                return;

            case DraftResult.StateConflict conflict:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", conflict.Reason);
                return;

            case DraftResult.InvalidInput input:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", input.Reason);
                return;
        }
    }

    private static Task NotSignedIn(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized,
            "Request failed", "authentication required");

    private static DraftV1 ToContract(ApplicationDraft draft) => new(
        draft.Id,
        draft.Job,
        draft.SubjectLine,
        draft.Text,
        EfDraftStore.StateWord(draft.State),
        draft.Version,
        draft.Error,
        draft.SharesResume,
        draft.Documents,
        [
            .. draft.Comments.Select(entry => new CommentV1(
                entry.Id, entry.Text, entry.Passage, entry.Resolved, entry.CreatedAt))
        ],
        draft.UpdatedAt,
        draft.WritingStartedAt);
}
