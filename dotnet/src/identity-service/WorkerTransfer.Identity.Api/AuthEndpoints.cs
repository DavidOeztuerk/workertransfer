using WorkerTransfer.Identity.Application.Anmelden;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to sign in.</summary>
public sealed record LoginBody(string Email, string Password);

/// <summary><c>POST /auth/{login,refresh,logout}</c>.</summary>
/// <remarks>
/// The cookies are set exactly as the Python service sets them — same names,
/// same paths, same flags — because the React app is not migrated and reads
/// them as they are. <c>access</c> is scoped to <c>/</c> and <c>refresh</c> to
/// <c>/auth</c>, so the refresh token only ever reaches the endpoints that
/// redeem it.
/// </remarks>
public static class AuthEndpoints
{
    private const string ZugriffsCookie = "access";
    private const string ErneuerungsCookie = "refresh";
    private const string ErneuerungsPfad = "/auth";

    /// <summary>Maps the three endpoints of the sign-in slice.</summary>
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var auth = app.MapGroup("/auth");

        auth.MapPost("/login", async (
            LoginBody body,
            AnmeldenHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var ergebnis = await handler.HandleAsync(
                body.Email, body.Password, cancellationToken);

            switch (ergebnis)
            {
                case Anmeldeergebnis.Angemeldet angemeldet:
                    SetzeCookies(context, angemeldet.Zugriffstoken, angemeldet.Erneuerungstoken);
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string> { ["status"] = "ok" }, cancellationToken);
                    return;

                case Anmeldeergebnis.NichtBestaetigt:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status403Forbidden,
                        "Request failed", "email not confirmed");
                    return;

                default:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status401Unauthorized,
                        "Request failed", "invalid credentials");
                    return;
            }
        });

        auth.MapPost("/refresh", async (
            ErneuernHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var vorgelegt = context.Request.Cookies[ErneuerungsCookie];

            if (string.IsNullOrEmpty(vorgelegt))
            {
                // No cookie to clear, and no cookie is not a dead cookie.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "invalid credentials");
                return;
            }

            var ergebnis = await handler.HandleAsync(vorgelegt, cancellationToken);

            if (ergebnis is Erneuerungsergebnis.Erneuert erneuert)
            {
                SetzeCookies(context, erneuert.Zugriffstoken, erneuert.Erneuerungstoken);
                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["status"] = "ok" }, cancellationToken);
                return;
            }

            // Clearing it breaks the loop: /auth/session reports "renewable"
            // while the cookie is there, the app refreshes, the token does not
            // carry, and the next page load starts over.
            LoescheErneuerungsCookie(context);
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "invalid credentials");
        });

        auth.MapPost("/logout", async (
            AbmeldenHandler handler,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            await handler.HandleAsync(
                context.Request.Cookies[ErneuerungsCookie], cancellationToken);

            LoescheErneuerungsCookie(context);

            // The access cookie goes too. Nothing checks it against the session
            // table, so leaving it would keep someone signed in for its whole
            // lifetime — on a shared machine that is the whole problem.
            context.Response.Cookies.Delete(ZugriffsCookie, new CookieOptions { Path = "/" });

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    private static void SetzeCookies(HttpContext context, string zugriff, string erneuerung)
    {
        var sicher = !context.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        context.Response.Cookies.Append(ZugriffsCookie, zugriff, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = sicher,
            Path = "/"
        });

        context.Response.Cookies.Append(ErneuerungsCookie, erneuerung, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = sicher,
            Path = ErneuerungsPfad
        });
    }

    private static void LoescheErneuerungsCookie(HttpContext context) =>
        context.Response.Cookies.Delete(ErneuerungsCookie, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = ErneuerungsPfad
        });
}
