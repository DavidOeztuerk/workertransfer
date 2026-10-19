using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.Identity.Infrastructure.Security;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary><c>/auth/external/*</c> — signing in with Google, Microsoft, Apple (ADR-0053).</summary>
/// <remarks>
/// <para><strong>The way back runs through this service, not through the
/// page.</strong> Apple answers with a form post, which a page cannot receive;
/// so for all three the callback lives here, exchanges the code, sets the
/// session like the password sign-in and sends the browser on to the page.</para>
///
/// <para><strong>The flow belongs to the browser that started it.</strong>
/// State, PKCE verifier, nonce and intent travel in a short-lived cookie only
/// this service can read. Without it, somebody could finish their own flow in
/// another person's browser and sign them into the wrong account.</para>
/// </remarks>
public static class ExternalLoginEndpoints
{
    private const string FlowCookie = "external_flow";
    private const string FlowPath = "/auth/external";
    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Maps the provider sign-in.</summary>
    public static IEndpointRouteBuilder MapExternalLoginEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var external = app.MapGroup(FlowPath);

        // What the page may ask on load: which buttons to draw. A statement
        // about this server's configuration, nothing personal.
        external.MapGet("", (IExternalProviders providers) =>
            Results.Json(new ProvidersV1([.. providers.Configured.Select(ExternalProviders.Word)])));

        external.MapGet("/{provider}/start", (
            string provider,
            string? intent,
            IExternalProviders providers,
            ICurrentPrincipal principal,
            IDataProtectionProvider protection,
            IOptions<MailSettings> mail,
            HttpContext context) =>
        {
            if (ExternalProviders.Read(provider) is not { } chosen || !providers.Configured.Contains(chosen))
            {
                return Results.NotFound();
            }

            var link = intent == "link";

            if (link && principal.Current is null)
            {
                return Results.Redirect(Page(mail, "/settings", "failed"));
            }

            var flow = new Flow(
                ExternalProviders.Word(chosen),
                HttpExternalProviders.Random(),
                HttpExternalProviders.Random(),
                HttpExternalProviders.Random(),
                link ? "link" : "signin",
                link ? principal.Current!.Subject.Value : null);

            context.Response.Cookies.Append(FlowCookie, Protector(protection).Protect(JsonSerializer.Serialize(flow)), new CookieOptions
            {
                HttpOnly = true,
                Path = FlowPath,
                MaxAge = FlowLifetime,
                // Apple comes back with a cross-site form post: only a
                // SameSite=None cookie rides along, and that must be Secure
                // (browsers accept it on http://localhost too).
                SameSite = chosen == ExternalProvider.Apple ? SameSiteMode.None : SameSiteMode.Lax,
                Secure = chosen == ExternalProvider.Apple || context.Request.IsHttps
            });

            return Results.Redirect(
                providers.AuthorizationUrl(chosen, flow.State, HttpExternalProviders.Challenge(flow.Verifier), flow.Nonce).ToString());
        });

        external.MapMethods("/{provider}/callback", ["GET", "POST"], async (
            string provider,
            IMediator mediator,
            IExternalProviders providers,
            IDataProtectionProvider protection,
            IOptions<MailSettings> mail,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // The same door as `start`: a provider without credentials has none.
            if (ExternalProviders.Read(provider) is not { } configured || !providers.Configured.Contains(configured))
            {
                return Results.NotFound();
            }

            var answer = await Answer(context, cancellationToken);
            var flow = ReadFlow(context, protection);
            context.Response.Cookies.Delete(FlowCookie, new CookieOptions { Path = FlowPath });

            var page = flow?.Intent == "link" ? "/settings" : "/login";

            if (flow is null
                || flow.Provider != provider
                || ExternalProviders.Read(provider) is not { } chosen
                || answer.Error is not null
                || string.IsNullOrEmpty(answer.Code)
                || !SameState(flow.State, answer.State))
            {
                return Results.Redirect(Page(mail, page, "failed"));
            }

            ExternalIdentity? identity;

            try
            {
                identity = await mediator.Send(
                    new ExchangeExternalCodeQuery(chosen, answer.Code, flow.Verifier, flow.Nonce), cancellationToken);
            }
            catch (ExternalProviderSilent)
            {
                return Results.Redirect(Page(mail, page, "unavailable"));
            }

            if (identity is null)
            {
                return Results.Redirect(Page(mail, page, "refused"));
            }

            // Apple names the person only on the first authorisation, and
            // only in the form — never in the token.
            if (answer.Name is { } name && identity.GivenName is null && identity.FamilyName is null)
            {
                identity = identity with { GivenName = name.Given, FamilyName = name.Family };
            }

            var result = await mediator.Send(
                new ExternalSignInCommand(
                    identity,
                    flow.Intent == "link" ? ExternalIntent.Link : ExternalIntent.SignIn,
                    flow.User is { } user ? new SubjectId(user) : null,
                    LanguageChoice.FromHeader(context.Request.Headers.AcceptLanguage.ToString())),
                cancellationToken);

            switch (result)
            {
                case ExternalSignInResult.SignedIn signedIn:
                    AuthEndpoints.SetCookies(context, signedIn.AccessToken, signedIn.RefreshToken);
                    return Results.Redirect($"{mail.Value.WebUrl.TrimEnd('/')}/overview");
                case ExternalSignInResult.Linked:
                    return Results.Redirect(Page(mail, "/settings", "linked"));
                case ExternalSignInResult.LinkTaken:
                    return Results.Redirect(Page(mail, "/settings", "link_taken"));
                case ExternalSignInResult.CheckMail:
                    return Results.Redirect(Page(mail, "/login", "check_mail"));
                case ExternalSignInResult.AddressTaken:
                    return Results.Redirect(Page(mail, "/login", "address_taken"));
                default:
                    return Results.Redirect(Page(mail, page, "refused"));
            }
        });

        external.MapGet("/links", async (
            IMediator mediator, ICurrentPrincipal principal, HttpContext context, CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var links = await mediator.Send(new ExternalLinksQuery(caller.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new LinksV1(
                    links.HasPassword,
                    [.. links.Linked.Select(login => new LinkV1(ExternalProviders.Word(login.Provider), login.CreatedAt))],
                    [.. links.Available.Select(ExternalProviders.Word)]),
                cancellationToken);
        });

        external.MapDelete("/links/{provider}", async (
            string provider, IMediator mediator, ICurrentPrincipal principal, HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (principal.Current is not { } caller)
            {
                await NotAuthenticated(context);
                return;
            }

            var result = ExternalProviders.Read(provider) is { } chosen
                ? await mediator.Send(new UnlinkExternalCommand(caller.Subject, chosen), cancellationToken)
                : UnlinkResult.NotLinked;

            switch (result)
            {
                case UnlinkResult.Unlinked:
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                    return;
                case UnlinkResult.LastWayIn:
                    // web/src/features/person/api/externalLogin.ts matches this sentence.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status409Conflict, "Request failed", "the last way to sign in stays");
                    return;
                default:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status404NotFound, "Request failed", "not linked");
                    return;
            }
        });

        return app;
    }

    private static IDataProtector Protector(IDataProtectionProvider protection) =>
        protection.CreateProtector("WorkerTransfer.Identity.ExternalLogin.Flow");

    private static Flow? ReadFlow(HttpContext context, IDataProtectionProvider protection)
    {
        if (context.Request.Cookies[FlowCookie] is not { Length: > 0 } sealedFlow)
        {
            return null;
        }

        try
        {
            var flow = JsonSerializer.Deserialize<Flow>(Protector(protection).Unprotect(sealedFlow));

            // The cookie's MaxAge is the browser's promise; this is ours.
            return flow is not null && DateTimeOffset.UtcNow - flow.IssuedAt <= FlowLifetime ? flow : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException)
        {
            return null;
        }
    }

    private static bool SameState(string expected, string? presented) =>
        presented is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(presented));

    /// <summary>Code, state and error — from the query (Google, Microsoft) or the form (Apple).</summary>
    private static async Task<CallbackAnswer> Answer(HttpContext context, CancellationToken cancellationToken)
    {
        if (HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
            return new CallbackAnswer(form["code"], form["state"], NullIfEmpty(form["error"]), HttpExternalProviders.AppleName(form["user"]));
        }

        var query = context.Request.Query;
        return new CallbackAnswer(query["code"], query["state"], NullIfEmpty(query["error"]), null);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static string Page(IOptions<MailSettings> mail, string path, string outcome) =>
        $"{mail.Value.WebUrl.TrimEnd('/')}{path}?external={outcome}";

    private static Task NotAuthenticated(HttpContext context) =>
        ProblemDetailsMiddleware.Write(context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    /// <summary>What the flow cookie carries — sealed with the service's data protection.</summary>
    private sealed record Flow(string Provider, string State, string Verifier, string Nonce, string Intent, Guid? User)
    {
        [JsonPropertyName("issued_at")]
        public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    /// <summary>What came back; <c>Name</c> is what Apple's form says of the person, on the first authorisation only.</summary>
    private sealed record CallbackAnswer(string? Code, string? State, string? Error, (string? Given, string? Family)? Name);

    private sealed record ProvidersV1([property: JsonPropertyName("providers")] IReadOnlyList<string> Providers);

    private sealed record LinkV1(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    private sealed record LinksV1(
        [property: JsonPropertyName("has_password")] bool HasPassword,
        [property: JsonPropertyName("linked")] IReadOnlyList<LinkV1> Linked,
        [property: JsonPropertyName("available")] IReadOnlyList<string> Available);
}
