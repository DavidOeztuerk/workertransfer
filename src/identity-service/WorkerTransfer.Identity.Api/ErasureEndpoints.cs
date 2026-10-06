using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary><c>POST /account/erasure</c>.</summary>
public static class ErasureEndpoints
{
    /// <summary>Maps the one endpoint that ends an account.</summary>
    public static IEndpointRouteBuilder MapErasureEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // No body at all. Self only — an id in the request would be a way to
        // delete somebody else's account — and no reason field, because
        // demanding a justification from somebody who wants to leave is a lever
        // against them (ADR-0027).
        app.MapPost("/account/erasure", async (
            IMediator mediator,
            ICurrentPrincipal principal,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } actor)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            await mediator.Send(
                new RequestErasureCommand(actor.Subject), cancellationToken);

            // The request revoked every access token of the person, this one
            // included — so the browser drops the cookies with it. A second
            // press therefore never gets this far (401), and none starts a
            // second cascade.
            AuthEndpoints.ClearSignInCookies(context);

            // "Running", never "done". The cascade is not instant, and
            // saying otherwise would be the first thing this promise breaks.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, string> { ["status"] = "accepted" }, cancellationToken);
        });

        return app;
    }
}
