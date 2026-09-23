using System.Text.Json.Serialization;
using MediatR;
using WorkerTransfer.Identity.Application.Registration;
using WorkerTransfer.Identity.Domain.Users;

using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to register.</summary>
/// <remarks>
/// <para><strong>The names stand there expressly, and that is no ornament.</strong>
/// This platform's wire is snake_case — nine services set it with
/// <c>[JsonPropertyName]</c> on their <c>Contracts</c> types, and the interface
/// sends accordingly. This body was the only one without them, fell back to
/// camelCase out of <c>NoeliaModule.JsonOptions</c> and bound
/// <c>display_name</c> to nothing.</para>
///
/// <para><strong>Why nobody noticed:</strong> every other body in identity has
/// nothing but single-word fields (<c>token</c>, <c>email</c>, <c>name</c>,
/// <c>role</c>) — there camelCase and snake_case are the same word. Only a
/// compound name makes the difference visible, and <c>display_name</c> is the
/// only one on a mandatory field.</para>
///
/// <para>It came back as a <c>500</c> (the column is <c>NOT NULL</c>), since H4
/// as a <c>422</c>. Both mean: through the interface nobody could register.
/// <c>WireNameTests</c> pins the four names now.</para>
/// </remarks>
/// <param name="OccupationalField">
/// Which working world the person stands in. Optional (ADR-0039) — making it
/// compulsory would be a hurdle before the first use.
/// </param>
/// <param name="CompanyName">
/// Set means "a company is registering here". Optional, because the ordinary
/// user of a transfer market is a person with no company at all.
/// </param>
public sealed record RegisterBody(
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("company_name")] string? CompanyName = null,
    [property: JsonPropertyName("given_name")] string? GivenName = null,
    [property: JsonPropertyName("family_name")] string? FamilyName = null,
    [property: JsonPropertyName("occupational_field")] string? OccupationalField = null);

/// <summary>What a caller sends to confirm.</summary>
public sealed record VerifyEmailBody(string Token);

/// <summary>What a caller sends to ask for a new link.</summary>
public sealed record ResendBody(string Email);

/// <summary><c>POST /auth/{register,verify-email,resend-verification}</c>.</summary>
public static class RegistrationEndpoints
{
    /// <summary>Maps the three endpoints of registering.</summary>
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var auth = app.MapGroup("/auth");

        auth.MapPost("/register", async (
            RegisterBody body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // The header is a FIRST GUESS here and nowhere else. The
            // confirmation mail is the first thing this account ever gets, and
            // it goes out before anybody could choose a language — without this
            // reach for it, it would always be German. From then on the language
            // stands in the row and the header is never read again: it is a
            // statement of the device, not a decision of the person.
            var guessed = LanguageChoice.FromHeader(
                context.Request.Headers.AcceptLanguage.ToString());

            // Refuse rather than discard: otherwise an account would come into
            // being, the mail would go out, and the choice would be silently
            // lost.
            if (!OccupationalFieldChoice.Known(body.OccupationalField))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: occupational_field");
                return;
            }

            var result = await mediator.Send(
                new RegisterCommand(
                    body.Email, body.Password, body.DisplayName, body.CompanyName,
                    guessed, body.GivenName, body.FamilyName,
                    OccupationalFieldChoice.From(body.OccupationalField)),
                cancellationToken);

            switch (result)
            {
                case RegistrationResult.Accepted:
                    // The same answer whether or not the address was known. A
                    // 409 would answer "is this person here?" without asking
                    // the consent ledger; the real owner gets a mail instead.
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string> { ["status"] = "registered" },
                        cancellationToken);
                    return;

                case RegistrationResult.PublicDomain publicDomain:
                    // The same code POST /companies answers with, so one
                    // refusal is not called two things depending on the door.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity, "Request failed",
                        $"'{publicDomain.Domain}' is a public email provider "
                        + "and cannot be claimed as a company");
                    return;

                case RegistrationResult.WeakPassword weak:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status400BadRequest, "Request failed",
                        $"Password rejected: {weak.Reason}");
                    return;

                default:
                    await ProblemDetailsMiddleware.Write(
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
            // A missing token is a malformed request, not an invalid token.
            // Without these lines `{}` came back as a 500 (D2).
            if (string.IsNullOrWhiteSpace(body.Token))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "token is required");
                return;
            }

            var result = await mediator.Send(
                new ConfirmAddressCommand(body.Token), cancellationToken);

            switch (result)
            {
                case ConfirmationResult.Confirmed confirmed:
                    var response = new Dictionary<string, string> { ["status"] = "ok" };

                    // Three exits, not two. Without the third an interface
                    // shows "all good" while half the intention evaporated.
                    if (confirmed.CompanyName is { } name)
                    {
                        response["company"] = name;
                    }

                    if (confirmed.CompanyError is { } error)
                    {
                        response["company_error"] = error;
                    }

                    await context.Response.WriteAsJsonAsync(response, cancellationToken);
                    return;

                case ConfirmationResult.Expired:
                    // 410 rather than 400, so the interface can offer to send
                    // it again. Only the recipient ever had the token, so the
                    // distinction reveals nothing.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status410Gone,
                        "Request failed", "confirmation link expired");
                    return;

                default:
                    await ProblemDetailsMiddleware.Write(
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
                new ResendConfirmationCommand(body.Email), cancellationToken);

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
