using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Application.Connections;
using WorkerTransfer.GitHub.Contracts;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Api;

/// <summary><c>/github/*</c> — claim, prove, show, disconnect.</summary>
/// <remarks>
/// No route here carries a score, a rank or a derived skill. What goes out is
/// evidence with links: what GitHub says about a <em>repository</em>. A
/// statement about a <em>human being</em> would be a judgement they cannot
/// comment on (ADR-0022).
/// </remarks>
public static class ConnectionEndpoints
{
    /// <summary>What a filter returns when it has written the response itself.</summary>
    /// <remarks>
    /// <strong>Not <c>null</c>.</strong> A filter that returns <c>null</c> lets
    /// the framework write once more — a JSON <c>null</c> along with headers,
    /// and those have already gone out by then. On a request with a body the
    /// connection tears.
    /// </remarks>
    private static readonly IResult AlreadyWritten = Results.Empty;

    /// <summary>Maps the routes of this service.</summary>
    public static IEndpointRouteBuilder MapConnectionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var github = app.MapGroup("/github");

        // Two silent dependencies, two different sentences — but the same
        // status code. Neither GitHub nor the ledger may become an assertion
        // about the person: "not proved", because we could not ask, would be
        // one.
        github.AddEndpointFilter(async (call, next) =>
        {
            try
            {
                return await next(call);
            }
            catch (GitHubSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "github unavailable");

                return AlreadyWritten;
            }
            catch (ConsentGateSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return AlreadyWritten;
            }
        });

        // Name the user name and get the one-time string. GitHub is NOT asked
        // here: for as long as nothing is proved there is nothing to fetch —
        // and a fetch would only tell GitHub that somebody asked about this
        // account.
        github.MapPost("/me", async (
            ConnectV1 body,
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

            var result = await mediator.Send(
                new ConnectCommand(actor.Subject, body.Login), cancellationToken);

            await Answer(context, result, own: true, cancellationToken);
        });

        github.MapPost("/me/verify", async (
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

            var result = await mediator.Send(
                new ProveCommand(actor.Subject), cancellationToken);

            await Answer(context, result, own: true, cancellationToken);
        });

        // THE PATH OVER GITHUB'S OWN SIGN-IN — it replaces the gist, not the
        // naming. Without stored credentials it answers `null`, and the
        // interface goes on offering the gist.
        github.MapPost("/me/oauth/start", async (
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

            var start = await mediator.Send(
                new StartSignInCommand(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new SignInStartV1(start.Url?.ToString()), cancellationToken);
        });

        // WITHOUT `/me`, BECAUSE NOTHING PERSONAL STANDS IN IT: the question is
        // whether this server has the sign-in configured. It creates nothing —
        // unlike `/me/oauth/start`, which is called on a button press for
        // exactly that reason and not on looking at the page.
        github.MapGet("/oauth", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // Signed in, although nothing personal stands in it: the answer
            // helps only with connecting, and only somebody signed in can do
            // that anyway. A new open door without a reason is one too many.
            if (principal.Current is null)
            {
                await NotAuthenticated(context);
                return;
            }

            var available = await mediator.Send(
                new SignInPossibleQuery(), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new SignInPossibleV1(available), cancellationToken);
        });

        github.MapPost("/me/oauth/finish", async (
            CompleteSignInV1 body,
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

            var result = await mediator.Send(
                new CompleteSignInCommand(
                    actor.Subject, body.Code, body.State),
                cancellationToken);

            await Answer(context, result, own: true, cancellationToken);
        });

        github.MapPost("/me/refresh", async (
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

            var result = await mediator.Send(
                new RefreshCommand(actor.Subject), cancellationToken);

            await Answer(context, result, own: true, cancellationToken);
        });

        // The unproved connection too — otherwise the person would see nothing
        // at all after the first step and would not know which string to write
        // into the gist.
        github.MapGet("/me", async (
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

            var connection = await mediator.Send(
                new MyConnectionQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                connection is null ? null : Response(connection, own: true), cancellationToken);
        });

        // Disconnecting means erasing — the snapshot goes with it.
        github.MapDelete("/me", async (
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

            await mediator.Send(new DisconnectCommand(actor.Subject), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        github.MapGet("/{subjectId:guid}", async (
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

            var connection = await mediator.Send(
                new VisibleConnectionQuery(new SubjectId(subjectId)), cancellationToken);

            if (connection is null)
            {
                // Not present, not proved, not released — the same answer. An
                // unproved connection is an assertion, and this service does
                // not show assertions.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                Response(connection, own: false), cancellationToken);
        });

        return app;
    }

    private static async Task Answer(
        HttpContext context,
        ConnectionResult result,
        bool own,
        CancellationToken cancellationToken)
    {
        switch (result)
        {
            case ConnectionResult.Done done:
                await context.Response.WriteAsJsonAsync(
                    Response(done.Connection, own), cancellationToken);
                return;

            case ConnectionResult.None:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;

            case ConnectionResult.NotProven:
                // 422, not 404: the request was fine, the proof was missing.
                //
                // The message no longer names the PATH. It used to read "no
                // public gist with that description was found" — right for as
                // long as there was only the gist, and misleading since the
                // sign-in over GitHub stands beside it: whoever authorises a
                // foreign account there would have read that their gist was
                // missing.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "the account was not proven");
                return;

            case ConnectionResult.InvalidInput invalid:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", invalid.Reason);
                return;

            default:
                throw new InvalidOperationException($"Unhandled result: {result}");
        }
    }

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static ConnectionV1 Response(Connection connection, bool own) =>
        new(connection.Subject.Value,
            connection.Login,
            connection.IsVerified,
            // Only in one's own view, and only for as long as it is not proved:
            // the one-time string is of use only to the person creating the
            // gist.
            own && !connection.IsVerified
                ? Connection.GistDescription(connection.Nonce)
                : null,
            connection.FetchedAt,
            [
                .. connection.Repositories.Select(entry => new RepositoryV1(
                    entry.Name, entry.Description, entry.Language,
                    entry.Stars, entry.Url, entry.LastPushedAt,
                    entry.Languages, entry.Topics))
            ],
            connection.LanguagesComplete);
}
