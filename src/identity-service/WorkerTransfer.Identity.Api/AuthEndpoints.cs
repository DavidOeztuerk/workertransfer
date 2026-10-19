using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.SignIn;

using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Identity.Domain.Users;
using System.Text.Json.Serialization;
using WorkerTransfer.Identity.Application.Account;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to sign in.</summary>
public sealed record LoginBody(string Email, string Password);

/// <summary>What a caller sends to change their settings.</summary>
/// <param name="DeleteAfterMonths">The lapse in months, or <c>null</c>.</param>
/// <param name="AiProvider">none (the platform's model), openai or anthropic.</param>
/// <param name="AiModel">The model; empty takes the provider's default.</param>
/// <param name="AiAuditLog">Whether requests are logged.</param>
/// <remarks>
/// No address: it belongs to the provider and is set by the platform
/// (ADR-0051). An <c>ai_base_url</c> an old client still sends is ignored.
/// </remarks>
public sealed record SettingsBody(
    [property: JsonPropertyName("delete_after_months")] int? DeleteAfterMonths,
    [property: JsonPropertyName("ai_provider")] string AiProvider = "none",
    [property: JsonPropertyName("ai_model")] string AiModel = "",
    [property: JsonPropertyName("ai_audit_log")] bool AiAuditLog = false);

/// <summary>What a caller sends to store a key.</summary>
/// <param name="Key">
/// The key in the clear. Empty means REMOVE — a field that does nothing when
/// empty has no way back to "none".
/// </param>
public sealed record AiKeyBody(
    [property: JsonPropertyName("key")] string? Key);

/// <summary>What a caller sends to change their language.</summary>
/// <param name="Language">A tag this platform has texts for: de, en or fr.</param>
public sealed record LanguageBody(string Language);

/// <summary>What a caller sends to choose their occupational field.</summary>
/// <param name="OccupationalField">
/// One of the labels from <see cref="OccupationalFieldChoice"/>. Empty means remove.
/// </param>
public sealed record OccupationalFieldBody(
    [property: JsonPropertyName("occupational_field")] string? OccupationalField);

/// <summary>The civil given and family name. Empty means remove.</summary>
public sealed record FullNameBody(
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("family_name")] string? FamilyName);

/// <summary>The application address. Never to a model, never into the token.</summary>
public sealed record PostalAddressBody(
    [property: JsonPropertyName("line1")] string? Line1,
    [property: JsonPropertyName("line2")] string? Line2,
    [property: JsonPropertyName("postal_code")] string? PostalCode,
    [property: JsonPropertyName("city")] string? City,
    [property: JsonPropertyName("country")] string? Country,
    [property: JsonPropertyName("phone")] string? Phone);

/// <summary><c>/auth/*</c> and <c>/me</c>.</summary>
/// <remarks>
/// The cookies keep the names, paths and flags the Python service used:
/// <c>access</c> scoped to <c>/</c> and <c>refresh</c> to <c>/auth</c>, so the
/// refresh token only ever reaches the endpoints that redeem it — and
/// <c>GET /auth/session</c>, which is under that path precisely so it can see
/// whether one is there.
/// </remarks>
public static class AuthEndpoints
{
    private const string AccessCookie = "access";
    private const string RefreshCookie = "refresh";
    private const string RefreshPath = "/auth";

    /// <summary>Maps the sign-in endpoints.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var auth = app.MapGroup("/auth");

        auth.MapPost("/login", async (
            LoginBody body,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // The check does NOT stand here any more but in `SignInValidator`
            // — Noelia's ValidationBehavior runs it before the handler runs.
            // Until D2 it stood here, because the pipeline stage was held to be
            // dead; it is not, it was only empty.
            //
            // The answer stays 422: the body was readable, its content was not
            // usable. 401 would mean "the credentials are wrong", and that
            // would be a statement about an account nobody checked here.
            var result = await mediator.Send(
                new SignInCommand(body.Email, body.Password), cancellationToken);

            switch (result)
            {
                case SignInResult.SignedIn signedIn:
                    SetCookies(context, signedIn.AccessToken, signedIn.RefreshToken);
                    await WriteOk(context, cancellationToken);
                    return;

                case SignInResult.NotConfirmed:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status403Forbidden,
                        "Request failed", "email not confirmed");
                    return;

                default:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status401Unauthorized,
                        "Request failed", "invalid credentials");
                    return;
            }
        });

        auth.MapGet("/session", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var state = await mediator.Send(
                new SessionStateQuery(
                    !string.IsNullOrEmpty(context.Request.Cookies[RefreshCookie])),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["user"] = state.Account is { } account ? AccountJson(account) : null,
                    ["state"] = state.State
                },
                cancellationToken);
        });

        auth.MapPost("/refresh", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var presented = context.Request.Cookies[RefreshCookie];

            if (string.IsNullOrEmpty(presented))
            {
                // No cookie to clear, and no cookie is not a dead cookie.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "invalid credentials");
                return;
            }

            var result = await mediator.Send(new RefreshCommand(presented), cancellationToken);

            if (result is RefreshResult.Refreshed refreshed)
            {
                SetCookies(context, refreshed.AccessToken, refreshed.RefreshToken);
                await WriteOk(context, cancellationToken);
                return;
            }

            // Clearing it breaks the loop: /auth/session reports "renewable"
            // while the cookie is there, the app refreshes, the token does not
            // carry, and the next page load starts over.
            ClearRefreshCookie(context);
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "invalid credentials");
        });

        auth.MapPost("/company/{tenantId:guid}", async (
            Guid tenantId,
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

            var result = await mediator.Send(
                new SwitchCompanyCommand(actor.Subject, new TenantId(tenantId)),
                cancellationToken);

            switch (result)
            {
                case CompanySwitchResult.Switched switched:
                    SetCookies(context, switched.AccessToken, switched.RefreshToken);
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string>
                        {
                            ["status"] = "ok",
                            ["tenant_id"] = tenantId.ToString()
                        },
                        cancellationToken);
                    return;

                case CompanySwitchResult.NoMember:
                    // 403 and not 404: never reveal whether the company exists.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status403Forbidden,
                        "Request failed", "not a member of this tenant");
                    return;

                default:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status401Unauthorized,
                        "Request failed", "invalid credentials");
                    return;
            }
        });

        auth.MapPost("/logout", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new SignOutCommand(
                    context.Request.Cookies[RefreshCookie],
                    context.User.FindFirst("jti")?.Value,
                    long.TryParse(context.User.FindFirst("exp")?.Value, out var expires)
                        ? DateTimeOffset.FromUnixTimeSeconds(expires)
                        : null),
                cancellationToken);

            ClearRefreshCookie(context);

            // The access cookie goes too. Nothing checks it against the session
            // table, so leaving it would keep someone signed in for its whole
            // lifetime — on a shared machine that is the whole problem.
            context.Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        // The language is a decision about one's own account and therefore
        // lives under `/account`, not under `/auth`: it has nothing to do with
        // signing in, and the five auth paths carry a brake that would only be
        // in the way here.
        app.MapPut("/account/language", async (
            LanguageBody body,
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

            // `LanguageChoice.From` answers anything unknown with the default
            // instead of with an error. Here that is wrong: whoever sends "is"
            // would silently get German stored and would not know why their
            // choice did not hold. A choice that does not take effect has to
            // say so.
            if (!LanguageChoice.Known(body.Language))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "unsupported language");
                return;
            }

            var done = await mediator.Send(
                new ChooseLanguageCommand(
                    actor.Subject, LanguageChoice.From(body.Language)),
                cancellationToken);

            if (!done)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await WriteOk(context, cancellationToken);
        });

        // Beside the language: a decision about one's own account.
        app.MapPut("/account/occupational-field", async (
            OccupationalFieldBody body,
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

            // Anything unknown is refused, not quietly turned into "none".
            if (!OccupationalFieldChoice.Known(body.OccupationalField))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: occupational_field");
                return;
            }

            var done = await mediator.Send(
                new ChooseOccupationalFieldCommand(
                    actor.Subject, OccupationalFieldChoice.From(body.OccupationalField)),
                cancellationToken);

            if (!done)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await WriteOk(context, cancellationToken);
        });

        app.MapPut("/account/name", async (
            FullNameBody body,
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

            var done = await mediator.Send(
                new SetFullNameCommand(actor.Subject, body.GivenName, body.FamilyName),
                cancellationToken);

            if (!done)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await WriteOk(context, cancellationToken);
        });

        app.MapGet("/account/address", async (
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

            var state = await mediator.Send(
                new PostalAddressQuery(actor.Subject), cancellationToken);
            await context.Response.WriteAsJsonAsync(AddressJson(state), cancellationToken);
        });

        app.MapPut("/account/address", async (
            PostalAddressBody body,
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

            var state = await mediator.Send(
                new SetPostalAddressCommand(
                    actor.Subject,
                    body.Line1,
                    body.Line2,
                    body.PostalCode,
                    body.City,
                    body.Country,
                    body.Phone),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(AddressJson(state), cancellationToken);
        });

        // One's own settings. Under `/account`, like the language and the
        // erasure — this is about the account, not about signing in.
        app.MapGet("/account/settings", async (
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

            var state = await mediator.Send(
                new SettingsQuery(actor.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(SettingsJson(state), cancellationToken);
        });

        app.MapPut("/account/settings", async (
            SettingsBody body,
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

            try
            {
                var state = await mediator.Send(
                    new SetSettingsCommand(
                        actor.Subject,
                        body.DeleteAfterMonths,
                        body.AiProvider,
                        body.AiModel,
                        body.AiAuditLog),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(SettingsJson(state), cancellationToken);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The lapse lies outside the bounds. 422 and not 400: the body
                // was readable, its content is not.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: delete_after_months");
            }
        });

        // The key separately, and with nothing returned. It travels one way.
        app.MapPut("/account/ai-key", async (
            AiKeyBody body,
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
                new SetAiKeyCommand(actor.Subject, body.Key ?? string.Empty),
                cancellationToken);

            await WriteOk(context, cancellationToken);
        });

        app.MapGet("/me", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var account = await mediator.Send(new MyAccountQuery(), cancellationToken);

            if (account is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            await context.Response.WriteAsJsonAsync(AccountJson(account), cancellationToken);
        });

        return app;
    }

    private static Dictionary<string, string?> AccountJson(AccountView account) => new()
    {
        ["user_id"] = account.Subject.ToString(),
        // The address is read from the account rather than carried in the
        // token, and it goes back only to the signed-in person themselves.
        ["email"] = account.Email,
        // null while acting as a person; a company is only active after
        // POST /auth/company/{id} (ADR-0017).
        ["tenant_id"] = account.Company?.ToString(),
        // So the interface follows the CHOSEN language and not the device.
        // Whoever picked German on an English machine should see German after
        // signing in — otherwise the choice would hold only for as long as the
        // same browser remembers it.
        ["language"] = LanguageChoice.Label(account.Language),
        // For the picture in the header: the initials come from the name, and
        // the colour beside it follows it. Both could be derived from an address
        // too — it would only be wrong, because `max.werber@…` gives "M" and
        // not "MW".
        ["display_name"] = account.DisplayName,
        // The civil name for a signature and a letterhead. No postal address
        // here: the candidate view reads the session, and the postal address
        // must not go to a model.
        ["given_name"] = account.GivenName,
        ["family_name"] = account.FamilyName,
        // So the interface knows what to offer (ADR-0039).
        ["occupational_field"] = OccupationalFieldChoice.Label(account.OccupationalField)
    };

    private static Dictionary<string, string> AddressJson(PostalAddressView state) => new()
    {
        ["line1"] = state.Line1,
        ["line2"] = state.Line2,
        ["postal_code"] = state.PostalCode,
        ["city"] = state.City,
        ["country"] = state.Country,
        ["phone"] = state.Phone
    };

    /// <summary>The settings on the wire — snake_case, as everywhere.</summary>
    private static Dictionary<string, object?> SettingsJson(SettingsView state) => new()
    {
        ["delete_after_months"] = state.DeleteAfterMonths,
        ["ai_provider"] = state.Provider,
        ["ai_base_url"] = state.Address,
        ["ai_model"] = state.Model,
        // NOT the key — only that there is one, and its last four characters.
        // A secret you can retrieve is one you can siphon off.
        ["ai_key_present"] = state.KeyPresent,
        ["ai_key_tail"] = state.KeyTail,
        ["ai_audit_log"] = state.AiAuditLog
    };

    private static Task WriteOk(HttpContext context, CancellationToken cancellationToken) =>
        context.Response.WriteAsJsonAsync(
            new Dictionary<string, string> { ["status"] = "ok" }, cancellationToken);

    internal static void SetCookies(HttpContext context, string access, string refresh)
    {
        var secure = !context.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        context.Response.Cookies.Append(AccessCookie, access, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            Path = "/"
        });

        context.Response.Cookies.Append(RefreshCookie, refresh, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = secure,
            Path = RefreshPath
        });
    }

    /// <summary>Takes both sign-in cookies out of the browser.</summary>
    /// <remarks>
    /// Once a token is revoked the browser must not keep presenting it: every
    /// page would then find a dead session and send the person away — the
    /// confirmation page of a new account included.
    /// </remarks>
    internal static void ClearSignInCookies(HttpContext context)
    {
        ClearRefreshCookie(context);
        context.Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });
    }

    private static void ClearRefreshCookie(HttpContext context) =>
        context.Response.Cookies.Delete(RefreshCookie, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = RefreshPath
        });
}
