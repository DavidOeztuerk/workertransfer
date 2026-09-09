using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Loeschung;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary><c>POST /account/erasure</c>.</summary>
public static class LoeschEndpoints
{
    /// <summary>Maps the one endpoint that ends an account.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // No body at all. Self only — an id in the request would be a way to
        // delete somebody else's account — and no reason field, because
        // demanding a justification from somebody who wants to leave is a lever
        // against them (ADR-0027).
        app.MapPost("/account/erasure", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            await mediator.Send(
                new LoeschungVerlangenBefehl(handelnder.Subject), cancellationToken);

            // 202 either way, including when it was already running: pressing
            // twice is not an error, and the second press must not start a
            // second cascade nor look like a failure.
            //
            // "läuft", never "erledigt". The cascade is not instant, and
            // saying otherwise would be the first thing this promise breaks.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, string> { ["status"] = "accepted" }, cancellationToken);
        });

        return app;
    }
}
