using MediatR;
using WorkerTransfer.Identity.Application.Registrierung;

using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to register.</summary>
/// <param name="CompanyName">
/// Set means "a company is registering here". Optional, because the ordinary
/// user of a transfer market is a person with no company at all.
/// </param>
public sealed record RegisterBody(
    string Email, string Password, string DisplayName, string? CompanyName = null);

/// <summary>What a caller sends to confirm.</summary>
public sealed record VerifyEmailBody(string Token);

/// <summary>What a caller sends to ask for a new link.</summary>
public sealed record ResendBody(string Email);

/// <summary><c>POST /auth/{register,verify-email,resend-verification}</c>.</summary>
public static class RegistrierungsEndpoints
{
    /// <summary>Maps the three endpoints of registering.</summary>
    public static IEndpointRouteBuilder MapRegistrierungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var auth = app.MapGroup("/auth");

        auth.MapPost("/register", async (
            RegisterBody body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var ergebnis = await mediator.Send(
                new RegistrierenBefehl(
                    body.Email, body.Password, body.DisplayName, body.CompanyName),
                cancellationToken);

            switch (ergebnis)
            {
                case Registrierergebnis.Angenommen:
                    // The same answer whether or not the address was known. A
                    // 409 would answer "is this person here?" without asking
                    // the consent ledger; the real owner gets a mail instead.
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string> { ["status"] = "registered" },
                        cancellationToken);
                    return;

                case Registrierergebnis.OeffentlicheDomain oeffentlich:
                    // The same code POST /companies answers with, so one
                    // refusal is not called two things depending on the door.
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status422UnprocessableEntity, "Request failed",
                        $"'{oeffentlich.Domain}' is a public email provider "
                        + "and cannot be claimed as a company");
                    return;

                case Registrierergebnis.SchwachesPasswort schwach:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status400BadRequest, "Request failed",
                        $"Password rejected: {schwach.Grund}");
                    return;

                default:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status400BadRequest, "Request failed", "invalid");
                    return;
            }
        });

        auth.MapPost("/verify-email", async (
            VerifyEmailBody body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // Ein fehlender Token ist eine kaputte Anfrage, kein ungueltiger
            // Token. Ohne diese Zeilen kam `{}` als 500 zurueck (D2).
            if (string.IsNullOrWhiteSpace(body.Token))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "token is required");
                return;
            }

            var ergebnis = await mediator.Send(
                new AdresseBestaetigenBefehl(body.Token), cancellationToken);

            switch (ergebnis)
            {
                case Bestaetigungsergebnis.Bestaetigt bestaetigt:
                    var antwort = new Dictionary<string, string> { ["status"] = "ok" };

                    // Three exits, not two. Without the third an interface
                    // shows "all good" while half the intention evaporated.
                    if (bestaetigt.Firmenname is { } name)
                    {
                        antwort["company"] = name;
                    }

                    if (bestaetigt.Firmenfehler is { } fehler)
                    {
                        antwort["company_error"] = fehler;
                    }

                    await context.Response.WriteAsJsonAsync(antwort, cancellationToken);
                    return;

                case Bestaetigungsergebnis.Abgelaufen:
                    // 410 rather than 400, so the interface can offer to send
                    // it again. Only the recipient ever had the token, so the
                    // distinction reveals nothing.
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status410Gone,
                        "Request failed", "confirmation link expired");
                    return;

                default:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status400BadRequest,
                        "Request failed", "invalid confirmation link");
                    return;
            }
        });

        auth.MapPost("/resend-verification", async (
            ResendBody body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new BestaetigungErneutSendenBefehl(body.Email), cancellationToken);

            // Always 202 — for an unknown address and for an account confirmed
            // long ago. Anything else makes this the enumeration channel that
            // /auth/register is built to close.
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, string> { ["status"] = "accepted" }, cancellationToken);
        });

        return app;
    }
}
