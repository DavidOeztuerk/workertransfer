using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Anmelden;

using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Identity.Domain.Users;
using System.Text.Json.Serialization;
using WorkerTransfer.Identity.Application.Konto;

namespace WorkerTransfer.Identity.Api;

/// <summary>What a caller sends to sign in.</summary>
public sealed record LoginBody(string Email, string Password);

/// <summary>Was ein Aufrufer schickt, um seine Einstellungen zu ändern.</summary>
/// <param name="DeleteAfterMonths">Verfall in Monaten, oder <c>null</c>.</param>
/// <param name="AiProvider">none, openai_compatible oder anthropic.</param>
/// <param name="AiBaseUrl">Die Adresse des Anbieters.</param>
/// <param name="AiModel">Das Modell.</param>
/// <param name="AiAuditLog">Ob Anfragen protokolliert werden.</param>
public sealed record SettingsBody(
    [property: JsonPropertyName("delete_after_months")] int? DeleteAfterMonths,
    [property: JsonPropertyName("ai_provider")] string AiProvider = "none",
    [property: JsonPropertyName("ai_base_url")] string AiBaseUrl = "",
    [property: JsonPropertyName("ai_model")] string AiModel = "",
    [property: JsonPropertyName("ai_audit_log")] bool AiAuditLog = false);

/// <summary>Was ein Aufrufer schickt, um einen Schlüssel zu hinterlegen.</summary>
/// <param name="Key">
/// Der Schlüssel im Klartext. Leer heisst ENTFERNEN — ein Feld, das bei leer
/// nichts tut, hat keinen Weg zurück zu „keiner".
/// </param>
public sealed record SchluesselBody(
    [property: JsonPropertyName("key")] string? Key);

/// <summary>What a caller sends to change their language.</summary>
/// <param name="Language">A tag this platform has texts for: de, en or fr.</param>
public sealed record SprachwahlBody(string Language);

/// <summary>Was ein Aufrufer schickt, um sein OccupationalField zu wählen.</summary>
/// <param name="OccupationalField">
/// Eines der Etiketten aus <see cref="Berufsfeldwahl"/>. Leer heisst entfernen.
/// </param>
public sealed record OccupationalFieldBody(
    [property: JsonPropertyName("occupational_field")] string? OccupationalField);

/// <summary>Bürgerlicher Vor- und Nachname. Leer heisst entfernen.</summary>
public sealed record KlarnameBody(
    [property: JsonPropertyName("given_name")] string? GivenName,
    [property: JsonPropertyName("family_name")] string? FamilyName);

/// <summary>Bewerbungsanschrift. Nie an ein Modell, nie ins Token.</summary>
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
            // Die Pruefung steht NICHT mehr hier, sondern in `SignInValidator`
            // — Noelias ValidationBehavior fuehrt sie aus, bevor der Handler
            // laeuft. Bis D2 stand sie hier, weil die Pipeline-Stufe fuer tot
            // gehalten wurde; sie ist es nicht, sie war nur leer.
            //
            // Die Antwort bleibt 422: der Rumpf war lesbar, sein Inhalt nicht
            // brauchbar. 401 hiesse "die Zugangsdaten stimmen nicht", und das
            // waere eine Aussage ueber ein Konto, die hier niemand geprueft hat.
            var result = await mediator.Send(
                new AnmeldenBefehl(body.Email, body.Password), cancellationToken);

            switch (result)
            {
                case Anmeldeergebnis.Angemeldet angemeldet:
                    SetzeCookies(context, angemeldet.Zugriffstoken, angemeldet.Erneuerungstoken);
                    await SchreibeOk(context, cancellationToken);
                    return;

                case Anmeldeergebnis.NichtBestaetigt:
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
                    !string.IsNullOrEmpty(context.Request.Cookies[ErneuerungsCookie])),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["user"] = state.Benutzer is { } konto ? Antwort(konto) : null,
                    ["state"] = state.Zustand
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "invalid credentials");
                return;
            }

            var result = await mediator.Send(new ErneuernBefehl(vorgelegt), cancellationToken);

            if (result is Erneuerungsergebnis.Erneuert erneuert)
            {
                SetzeCookies(context, erneuert.Zugriffstoken, erneuert.Erneuerungstoken);
                await SchreibeOk(context, cancellationToken);
                return;
            }

            // Clearing it breaks the loop: /auth/session reports "renewable"
            // while the cookie is there, the app refreshes, the token does not
            // carry, and the next page load starts over.
            LoescheErneuerungsCookie(context);
            await ProblemDetailsMiddleware.Write(
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            var result = await mediator.Send(
                new SwitchCompanyCommand(handelnder.Subject, new TenantId(tenantId)),
                cancellationToken);

            switch (result)
            {
                case CompanySwitchResult.Gewechselt gewechselt:
                    SetzeCookies(context, gewechselt.Zugriffstoken, gewechselt.Erneuerungstoken);
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
                new AbmeldenBefehl(context.Request.Cookies[ErneuerungsCookie]), cancellationToken);

            LoescheErneuerungsCookie(context);

            // The access cookie goes too. Nothing checks it against the session
            // table, so leaving it would keep someone signed in for its whole
            // lifetime — on a shared machine that is the whole problem.
            context.Response.Cookies.Delete(AccessCookie, new CookieOptions { Path = "/" });

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        // Die Sprache ist eine Entscheidung über das eigene Konto und liegt
        // deshalb unter `/account`, nicht unter `/auth`: sie hat mit dem
        // Anmelden nichts zu tun, und die fünf Auth-Pfade tragen eine Bremse,
        // die hier nur im Weg stünde.
        app.MapPut("/account/language", async (
            SprachwahlBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            // `Sprachwahl.Aus` beantwortet Unbekanntes mit der Vorgabe statt mit
            // einem Fehler. Hier ist das falsch: wer „is" schickt, bekäme
            // stillschweigend Deutsch gespeichert und wüsste nicht, warum seine
            // Wahl nicht hielt. Eine Wahl, die nicht wirkt, muss das sagen.
            if (!Sprachwahl.Kennen(body.Language))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "unsupported language");
                return;
            }

            var erledigt = await mediator.Send(
                new SpracheWaehlenBefehl(
                    handelnder.Subject, Sprachwahl.Aus(body.Language)),
                cancellationToken);

            if (!erledigt)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await SchreibeOk(context, cancellationToken);
        });

        // Neben der Sprache: eine Entscheidung über das eigene Konto.
        app.MapPut("/account/occupational-field", async (
            OccupationalFieldBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            // Unbekanntes wird abgesagt, nicht still zu „keins" gemacht.
            if (!OccupationalFieldChoice.Kennen(body.OccupationalField))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: occupational_field");
                return;
            }

            var erledigt = await mediator.Send(
                new ChooseOccupationalFieldCommand(
                    handelnder.Subject, OccupationalFieldChoice.Aus(body.OccupationalField)),
                cancellationToken);

            if (!erledigt)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await SchreibeOk(context, cancellationToken);
        });

        app.MapPut("/account/name", async (
            KlarnameBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            var erledigt = await mediator.Send(
                new KlarnameSetzenBefehl(handelnder.Subject, body.GivenName, body.FamilyName),
                cancellationToken);

            if (!erledigt)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such account");
                return;
            }

            await SchreibeOk(context, cancellationToken);
        });

        app.MapGet("/account/address", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            var state = await mediator.Send(
                new PostalAddressQuery(handelnder.Subject), cancellationToken);
            await context.Response.WriteAsJsonAsync(AnschriftAntwort(state), cancellationToken);
        });

        app.MapPut("/account/address", async (
            PostalAddressBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            var state = await mediator.Send(
                new SetPostalAddressCommand(
                    handelnder.Subject,
                    body.Line1,
                    body.Line2,
                    body.PostalCode,
                    body.City,
                    body.Country,
                    body.Phone),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(AnschriftAntwort(state), cancellationToken);
        });

        // Die eigenen Einstellungen. Unter `/account`, wie die Sprache und die
        // Löschung — es geht um das Konto, nicht um das Anmelden.
        app.MapGet("/account/settings", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            var state = await mediator.Send(
                new SettingsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Einstellungen(state), cancellationToken);
        });

        app.MapPut("/account/settings", async (
            SettingsBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
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
                        handelnder.Subject,
                        body.DeleteAfterMonths,
                        body.AiProvider,
                        body.AiBaseUrl,
                        body.AiModel,
                        body.AiAuditLog),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Einstellungen(state), cancellationToken);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Der Verfall liegt ausserhalb der Grenzen. 422 und nicht 400:
                // der Rumpf war lesbar, sein Inhalt ist es nicht.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: delete_after_months");
            }
        });

        // Der Schlüssel getrennt, und ohne Rückgabe. Er geht in eine Richtung.
        app.MapPut("/account/ai-key", async (
            SchluesselBody body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authenticated");
                return;
            }

            await mediator.Send(
                new SchluesselSetzenBefehl(handelnder.Subject, body.Key ?? string.Empty),
                cancellationToken);

            await SchreibeOk(context, cancellationToken);
        });

        app.MapGet("/me", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var konto = await mediator.Send(new MyAccountQuery(), cancellationToken);

            if (konto is null)
            {
                await ProblemDetailsMiddleware.Write(
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
        ["tenant_id"] = konto.Firma?.ToString(),
        // Damit die Oberfläche der GEWÄHLTEN Sprache folgt und nicht dem Gerät.
        // Wer auf einem englischen Rechner Deutsch gewählt hat, soll nach dem
        // Anmelden Deutsch sehen — sonst wäre die Wahl nur so lange gültig, wie
        // derselbe Browser sie sich merkt.
        ["language"] = Sprachwahl.Etikett(konto.Sprache),
        // Fuer das Bild im Header: aus dem Namen werden die Initialen, und die
        // Farbe daneben folgt ihm. Aus einer Adresse liesse sich beides auch
        // ableiten — es waere nur falsch, denn `max.werber@…` ergibt „M" und
        // nicht „MW".
        ["display_name"] = konto.Anzeigename,
        // Klarname für Signatur und Briefkopf. None PostalAddress hier: die
        // Bewerberauskunft liest die Session, und PostalAddress darf nicht ins Modell.
        ["given_name"] = konto.Vorname,
        ["family_name"] = konto.Nachname,
        // Damit die Oberfläche weiss, was sie anbieten soll (ADR-0039).
        ["occupational_field"] = OccupationalFieldChoice.Etikett(konto.OccupationalField)
    };

    private static Dictionary<string, string> AnschriftAntwort(PostalAddressView state) => new()
    {
        ["line1"] = state.Zeile1,
        ["line2"] = state.Zeile2,
        ["postal_code"] = state.Postleitzahl,
        ["city"] = state.Ort,
        ["country"] = state.Land,
        ["phone"] = state.Telefon
    };

    /// <summary>Die Einstellungen auf dem Draht — snake_case, wie überall.</summary>
    private static Dictionary<string, object?> Einstellungen(SettingsView state) => new()
    {
        ["delete_after_months"] = state.LoeschungNachMonaten,
        ["ai_provider"] = state.Anbieter,
        ["ai_base_url"] = state.Adresse,
        ["ai_model"] = state.Modell,
        // NICHT der Schlüssel — nur, dass es einen gibt, und seine letzten vier
        // Zeichen. Ein Geheimnis, das man abrufen kann, ist eines, das man
        // abziehen kann.
        ["ai_key_present"] = state.SchluesselDa,
        ["ai_key_tail"] = state.SchluesselEndung,
        ["ai_audit_log"] = state.KiProtokoll
    };

    private static Task SchreibeOk(HttpContext context, CancellationToken cancellationToken) =>
        context.Response.WriteAsJsonAsync(
            new Dictionary<string, string> { ["status"] = "ok" }, cancellationToken);

    private static void SetzeCookies(HttpContext context, string zugriff, string erneuerung)
    {
        var sicher = !context.RequestServices
            .GetRequiredService<IHostEnvironment>().IsDevelopment();

        context.Response.Cookies.Append(AccessCookie, zugriff, new CookieOptions
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
