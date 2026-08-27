using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Anmelden;

using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to sign in.</summary>
public sealed record LoginBody(string Email, string Password);

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
    private const string ZugriffsCookie = "access";
    private const string ErneuerungsCookie = "refresh";
    private const string ErneuerungsPfad = "/auth";

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
            // Fehlt eines der beiden Felder, ist das eine kaputte Anfrage und
            // keine falsche Anmeldung. Ohne diese Zeilen lief `null` bis in den
            // Passwortpruefer und kam als 500 zurueck — gemessen an D2, mit
            // `{}` als Rumpf.
            //
            // 422 und nicht 401: 401 hiesse "die Zugangsdaten stimmen nicht",
            // und das waere eine Aussage ueber ein Konto, die hier niemand
            // geprueft hat. Ueber die Existenz einer Adresse sagt beides
            // nichts — genau darum bleibt die Meldung bei "field missing".
            if (string.IsNullOrWhiteSpace(body.Email)
                || string.IsNullOrWhiteSpace(body.Password))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "email and password are required");
                return;
            }

            var ergebnis = await mediator.Send(
                new AnmeldenBefehl(body.Email, body.Password), cancellationToken);

            switch (ergebnis)
            {
                case Anmeldeergebnis.Angemeldet angemeldet:
                    SetzeCookies(context, angemeldet.Zugriffstoken, angemeldet.Erneuerungstoken);
                    await SchreibeOk(context, cancellationToken);
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

        auth.MapGet("/session", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var stand = await mediator.Send(
                new SitzungsstandAbfrage(
                    !string.IsNullOrEmpty(context.Request.Cookies[ErneuerungsCookie])),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["user"] = stand.Benutzer is { } konto ? Antwort(konto) : null,
                    ["state"] = stand.Zustand
                },
                cancellationToken);
        });

        auth.MapPost("/refresh", async (
            IMediator mediator,
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

            var ergebnis = await mediator.Send(new ErneuernBefehl(vorgelegt), cancellationToken);

            if (ergebnis is Erneuerungsergebnis.Erneuert erneuert)
            {
                SetzeCookies(context, erneuert.Zugriffstoken, erneuert.Erneuerungstoken);
                await SchreibeOk(context, cancellationToken);
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

        auth.MapPost("/company/{tenantId:guid}", async (
            Guid tenantId,
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

            var ergebnis = await mediator.Send(
                new FirmaWechselnBefehl(handelnder.Subject, new TenantId(tenantId)),
                cancellationToken);

            switch (ergebnis)
            {
                case Firmenwechselergebnis.Gewechselt gewechselt:
                    SetzeCookies(context, gewechselt.Zugriffstoken, gewechselt.Erneuerungstoken);
                    await context.Response.WriteAsJsonAsync(
                        new Dictionary<string, string>
                        {
                            ["status"] = "ok",
                            ["tenant_id"] = tenantId.ToString()
                        },
                        cancellationToken);
                    return;

                case Firmenwechselergebnis.KeinMitglied:
                    // 403 and not 404: never reveal whether the company exists.
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status403Forbidden,
                        "Request failed", "not a member of this tenant");
                    return;

                default:
                    await ProblemDetailsMiddleware.Schreibe(
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
                new AbmeldenBefehl(context.Request.Cookies[ErneuerungsCookie]), cancellationToken);

            LoescheErneuerungsCookie(context);

            // The access cookie goes too. Nothing checks it against the session
            // table, so leaving it would keep someone signed in for its whole
            // lifetime — on a shared machine that is the whole problem.
            context.Response.Cookies.Delete(ZugriffsCookie, new CookieOptions { Path = "/" });

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapGet("/me", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var konto = await mediator.Send(new MeinKontoAbfrage(), cancellationToken);

            if (konto is null)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(konto), cancellationToken);
        });

        return app;
    }

    private static Dictionary<string, string?> Antwort(Kontoansicht konto) => new()
    {
        ["user_id"] = konto.Wer.ToString(),
        // The address is read from the account rather than carried in the
        // token, and it goes back only to the signed-in person themselves.
        ["email"] = konto.Email,
        // null while acting as a person; a company is only active after
        // POST /auth/company/{id} (ADR-0017).
        ["tenant_id"] = konto.Firma?.ToString()
    };

    private static Task SchreibeOk(HttpContext context, CancellationToken cancellationToken) =>
        context.Response.WriteAsJsonAsync(
            new Dictionary<string, string> { ["status"] = "ok" }, cancellationToken);

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
