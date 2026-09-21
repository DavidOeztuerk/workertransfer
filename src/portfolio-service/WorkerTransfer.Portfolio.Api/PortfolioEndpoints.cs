using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Application.Portfolios;
using WorkerTransfer.Portfolio.Contracts;
using WorkerTransfer.Portfolio.Domain.Portfolios;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Api;

/// <summary><c>/portfolios/*</c> — writing, reading, work samples.</summary>
public static class PortfolioEndpoints
{
    /// <summary>How large an uploaded file may be.</summary>
    private const long MaxFileSize = 10 * 1024 * 1024;

    /// <summary>Binds the five portfolio routes.</summary>
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // A silent ledger is neither a yes nor a no. As a filter over the
        // whole group, because the route that forgets it looks like a working
        // one until the ledger goes out.
        var portfolios = app.MapGroup("/portfolios").AddEndpointFilter(
            async (call, next) =>
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

                    return AlreadyWritten;
                }
            });

        portfolios.MapPut("/me", async (
            WritePortfolioV1 body,
            IMediator mediator,
            ICurrentPrincipal principal,
            TimeProvider clock,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await NotAuthenticated(context);
                return;
            }

            try
            {
                var portfolio = await mediator.Send(
                    new SavePortfolioCommand(actor.Subject, Entries(body, clock)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Response(portfolio), cancellationToken);
            }
            catch (InputError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        portfolios.MapGet("/me", async (
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

            // Without asking the ledger: the release governs who sees it from
            // outside, not whether somebody may read their own.
            var portfolio = await mediator.Send(
                new MyPortfolioQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                portfolio is null ? null : Response(portfolio), cancellationToken);
        });

        portfolios.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is null)
            {
                await NotAuthenticated(context);
                return;
            }

            var portfolio = await mediator.Send(
                new OtherPortfolioQuery(new SubjectId(subjectId)), cancellationToken);

            if (portfolio is null)
            {
                // Hidden and non-existent answer the same, and that must stay
                // so: a difference would say whether this person has shown
                // anything here.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such portfolio");
                return;
            }

            await context.Response.WriteAsJsonAsync(Response(portfolio), cancellationToken);
        });

        portfolios.MapPost("/me/attachments", async (
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

            if (!context.Request.HasFormContentType)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status415UnsupportedMediaType,
                    "Request failed", "expected a multipart upload");
                return;
            }

            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");

            if (file is null || file.Length == 0)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "no file");
                return;
            }

            // A limit, because without one the first video file fills the
            // disk — and because a portfolio is a shop window, not an archive.
            if (file.Length > MaxFileSize)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "Request failed", "the file is too large");
                return;
            }

            await using var content = file.OpenReadStream();

            // The name comes back from the store, not from the client: a name
            // chosen by the caller would be a path somebody can shape.
            var name = await mediator.Send(
                new StoreAttachmentCommand(
                    actor.Subject, file.FileName, file.ContentType, content),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, string> { ["attachment"] = name }, cancellationToken);
        });

        portfolios.MapGet("/{subjectId:guid}/attachments/{name}", async (
            Guid subjectId,
            string name,
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

            var stored = await mediator.Send(
                new AttachmentQuery(new SubjectId(subjectId), name, actor.Subject),
                cancellationToken);

            if (stored is null)
            {
                // The same answer whether the file is missing, the portfolio
                // is hidden or no entry names it. Three reasons, one answer.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such attachment");
                return;
            }

            await using (stored)
            {
                context.Response.ContentType = stored.MediaType;

                // No rendering in the browser: an uploaded file a stranger
                // opens is downloaded and not executed in our origin.
                context.Response.Headers.ContentDisposition = $"attachment; filename=\"{name}\"";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                await stored.Content.CopyToAsync(context.Response.Body, cancellationToken);
            }
        });

        return app;
    }

    /// <summary>
    /// What a filter returns when it has written the response itself.
    /// </summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> along with headers,
    /// and those have already gone out by then. On a request without a body the
    /// caller still sees its 503 and only the log carries an unhandled
    /// exception; on one with a body the connection tears, and the caller gets
    /// "Error while copying content to a stream". Measured at
    /// <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult AlreadyWritten = Results.Empty;

    private static IReadOnlyList<Entry> Entries(
        WritePortfolioV1 body, TimeProvider clock)
    {
        var now = clock.GetUtcNow();

        return [.. (body.Items ?? []).Select(entry => Entry.From(
            entry.Title, now, entry.Summary, entry.Url,
            entry.Role, entry.Year, entry.Attachment, entry.Technologies))];
    }

    private static PortfolioV1 Response(Domain.Portfolios.Portfolio portfolio) => new(
        portfolio.Subject.Value,
        [.. portfolio.Entries.Select(entry => new EntryV1(
            entry.Title, entry.Summary, entry.Link,
            entry.Role, entry.Year, entry.Attachment, entry.Technologies))],
        portfolio.ChangedAt);

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
