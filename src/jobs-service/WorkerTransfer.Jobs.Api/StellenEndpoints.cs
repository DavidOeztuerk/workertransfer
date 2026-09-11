using System.Globalization;
using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Api.Berechtigung;
using WorkerTransfer.Jobs.Application.Entwurf;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Stellen;
using WorkerTransfer.Jobs.Contracts;
using WorkerTransfer.Jobs.Domain.Stellen;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Rollen;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>/jobs</c> und <c>/companies/me/jobs</c>.</summary>
public static class StellenEndpoints
{
    /// <summary>Bindet die sieben Stellenrouten ein.</summary>
    public static IEndpointRouteBuilder MapStellenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var stellen = app.MapGroup("/jobs");

        stellen.MapPost("/draft", async (
            EntwurfV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            if (handelnder.Acting is not Capacity.ForCompany)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            try
            {
                // Die tenant_id wird ABSICHTLICH nicht weitergereicht: der
                // Entwurfskontext hat kein Feld dafür, also kann auch nichts
                // hineinrutschen (ADR-0024).
                var text = await mediator.Send(
                    new AnzeigeEntwerfenAbfrage(
                        koerper.Title, koerper.Description, koerper.Skills ?? [],
                        koerper.Location, koerper.Wish),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (EntwurfNichtVerfuegbar fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", fehler.Message);
            }
            catch (Eingabefehler fehler)
            {
                // Ein zu langer Wunsch ist eine Eingabe, kein Ausfall: 422, nicht
                // 503. Ohne diesen Zweig verliesse er den Dienst als 500 — und
                // ein 500 sagt dem Aufrufer, es liege nicht an ihm.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        stellen.MapPost("/", async (
            StelleSchreibenV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var stelle = await mediator.Send(
                    new StelleAnlegenBefehl(firma, Angaben(koerper)), cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(Antwort(stelle), cancellationToken);
            });
        });

        stellen.MapPut("/{id:guid}", async (
            Guid id,
            StelleSchreibenV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var stelle = await mediator.Send(
                    new StelleAendernBefehl(firma, id, Angaben(koerper)), cancellationToken);

                await Zeige(context, stelle, cancellationToken);
            });
        });

        // Die beiden Zustandswechsel sind die einzigen Stellen hier, an denen
        // die Anzeige das Unternehmen nach aussen vertritt — und deshalb die
        // einzigen, die einen `admin` verlangen. Schreiben und Aendern bleiben
        // jedem Mitglied: ein Entwurf steht niemandem gegenueber.
        //
        // Aufgeloest wird die Richtlinie von `Adminrecht` aus der
        // Mitgliedschaftstabelle von identity-service, nicht aus dem Token —
        // wer entfernt wird, ist bei der naechsten Anfrage draussen.
        stellen.MapPost("/{id:guid}/publish", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, Stellenstand.Published, mediator, akteur, context, cancellationToken))
            .RequireAuthorization(
                AdminrechteErweiterungen.Richtlinie(Stellenrechte.Veroeffentlichen));

        stellen.MapPost("/{id:guid}/close", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, Stellenstand.Closed, mediator, akteur, context, cancellationToken))
            .RequireAuthorization(
                AdminrechteErweiterungen.Richtlinie(Stellenrechte.Schliessen));

        // Öffentlich für jeden Angemeldeten — und ohne jede Sortierung nach
        // Passung. Wie gut jemand zu einer Stelle passt, rechnet der Browser
        // und zeigt es der Person (ADR-0022).
        // OHNE Anmeldung, und das ist eine Korrektur.
        //
        // Hier stand eine Sperre auf `akteur.Current is null`. Sie gab nichts
        // preis, was nicht ohnehin oeffentlich waere — der Speicher filtert auf
        // `status == "published"`, und veroeffentlicht heisst in dieser Domaene
        // "fuer alle sichtbar, auch ohne Konto". Sie kostete aber genau das,
        // wofuer eine Anzeige da ist: `/careers/<kuerzel>` ist als oeffentliche
        // Seite dokumentiert (docs/routenkarte.yml) und zeigte einem anonymen
        // Besucher trotzdem keine einzige Stelle. Eine Ausschreibung, die man
        // nur nach Anmeldung sieht, erreicht genau die nicht, fuer die sie
        // gedacht ist.
        //
        // Entwuerfe und geschlossene Anzeigen bleiben drinnen: der Stand wird
        // im Speicher gefiltert und nie vom Aufrufer.
        stellen.MapGet("/", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var anfrage = context.Request.Query;

            // `q`, `company` und `employment` wurden von der Oberflaeche seit
            // jeher geschickt und hier NIE gelesen. Die Suchmaske suchte also
            // nichts, der Beschaeftigungsfilter filterte nichts — und die
            // Karriereseite zeigte die Anzeigen ALLER Unternehmen unter dem
            // Namen eines einzigen.
            var ort = anfrage["location"].ToString();

            // Der Umkreis kommt als drei Zahlen und wird HIER zu einem Wert
            // oder zu nichts. `lat` und `lon` sind bereits im Browser gerundet
            // (zwei Nachkommastellen, gut ein Kilometer) — sie stehen in einer
            // Adresszeile und damit in Zugriffsprotokollen, und genauer könnte
            // an keiner Antwort etwas ändern, weil die Orte der Anzeigen
            // Stadtmittelpunkte sind.
            var radius = Zahl(anfrage["radius_km"]);

            var umkreis = Umkreis.Aus(Zahl(anfrage["lat"]), Zahl(anfrage["lon"]), radius)
                // OHNE Koordinaten ist der ORT die Mitte. Wer „Leipzig" tippt
                // und „50 km" wählt, meint „um Leipzig herum" — und braucht
                // dafür weder GPS noch die Erlaubnis dazu.
                ?? (Ortskunde.Finde(null, ort) is { } mitte
                    ? Umkreis.Aus(mitte.Breite, mitte.Laenge, radius)
                    : null);

            var seite = await mediator.Send(
                new StellensucheAbfrage(
                    Seitenwahl.Seite(anfrage["page"]),
                    Seitenwahl.Groesse(anfrage["page_size"]),
                    anfrage["skill"].Count > 0 ? [.. anfrage["skill"]!] : null,
                    // MIT Umkreis ist der Ort die MITTE und kein Textfilter.
                    // Beides zugleich hiesse „innerhalb von 25 km UND das Wort
                    // muss vorkommen" — und schlösse genau die Nachbarorte aus,
                    // wegen derer jemand einen Umkreis wählt.
                    umkreis is null ? ort : string.Empty,
                    Grad(anfrage["remote"]),
                    anfrage["q"].ToString(),
                    Guid.TryParse(anfrage["company"], out var firma) ? firma : null,
                    anfrage["employment"].ToString(),
                    umkreis),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Seitenantwort<StelleV1>(
                    [.. seite.Eintraege.Select(Antwort)],
                    Seitenwahl.Seite(anfrage["page"]),
                    Seitenwahl.Groesse(anfrage["page_size"]),
                    seite.Gesamt,
                    // Nur bei einer Umkreissuche. Ohne sie wurde nichts
                    // ausgelassen, und eine `0` behauptete, die Frage sei
                    // gestellt worden.
                    umkreis is null ? null : seite.OhneOrt),
                cancellationToken);
        });

        // Der Ort zu einem Punkt — die Gegenrichtung der Umkreissuche.
        //
        // Ohne Anmeldung, wie die Liste selbst: geantwortet wird aus einer
        // Tabelle, die im Bild mitreist (ADR-0032). Sie sagt nichts ueber
        // irgendjemanden, und der Punkt, der hereinkommt, ist derselbe bereits
        // gerundete, der auch an die Suche geht.
        stellen.MapGet("/place", async (
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var punkt = Zahl(context.Request.Query["lat"]) is { } breite
                        && Zahl(context.Request.Query["lon"]) is { } laenge
                        && breite is >= -90 and <= 90
                        && laenge is >= -180 and <= 180
                ? new Ortspunkt(breite, laenge)
                : (Ortspunkt?)null;

            // Kein Ort in der Naehe ist kein Fehler, sondern eine Auskunft: der
            // Punkt liegt ausserhalb von DE, AT und CH. Ein 404 waere hier
            // irrefuehrend — die Adresse gibt es.
            await context.Response.WriteAsJsonAsync(
                new { location = punkt is { } p ? Ortskunde.NaechsterOrt(p) : null },
                cancellationToken);
        });

        // Ohne Anmeldung lesbar, genau wie die Liste — und aus denselben zwei
        // Gruenden. Die Karriereseite ist eine Adresse, die man weitergibt: wer
        // sie oeffnet, hat kein Konto, und ein 401 machte aus der Anzeige eine
        // Anmeldeaufforderung. Und applications-service fragt hier
        // Dienst-zu-Dienst nach, ob es die Stelle gibt — ohne Token, weil er
        // keines hat; das Tor liess deshalb JEDE Bewerbung mit 503 enden.
        //
        // Sicher ist das ohne Zutun: der Handler gibt eine Stelle nur heraus,
        // wenn sie dem fragenden Unternehmen gehoert ODER veroeffentlicht ist.
        // Ohne Token ist die Firma `null`, also bleibt allein das
        // Veroeffentlichte — Entwurf, geschlossen und nicht vorhanden sind
        // ununterscheidbar 404.
        stellen.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var stelle = await mediator.Send(
                new StelleAbfrage(
                    id,
                    akteur.Current?.Acting is Capacity.ForCompany firma ? firma.Tenant : null),
                cancellationToken);

            await Zeige(context, stelle, cancellationToken);
        });

        app.MapGet("/companies/me/jobs", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            var meine = await mediator.Send(new MeineStellenAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort), cancellationToken);
        });

        return app;
    }

    private static async Task Wechsle(
        Guid id,
        Stellenstand ziel,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Firma(akteur, out var firma))
        {
            await KeineFirma(context, akteur);
            return;
        }

        await Beantworte(context, cancellationToken, async () =>
        {
            var stelle = await mediator.Send(
                new StandAendernBefehl(firma, id, ziel), cancellationToken);

            await Zeige(context, stelle, cancellationToken);
        });
    }

    /// <summary>
    /// Zeigt sie, oder antwortet gleichlautend für „gibt es nicht" und „gehört
    /// einem anderen Unternehmen".
    /// </summary>
    /// <remarks>
    /// Ein Unterschied verriete, welche Anzeigen es anderswo gibt — und ein
    /// Entwurf ist gerade das, was ein Unternehmen noch nicht zeigen will.
    /// </remarks>
    private static Task Zeige(HttpContext context, Stelle? stelle, CancellationToken abbruch) =>
        stelle is null
            ? ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status404NotFound, "Request failed", "no such job")
            : context.Response.WriteAsJsonAsync(Antwort(stelle), abbruch);

    private static async Task Beantworte(
        HttpContext context, CancellationToken abbruch, Func<Task> arbeit)
    {
        try
        {
            await arbeit();
        }
        catch (UebergangNichtErlaubt fehler)
        {
            // 409, nicht 422: der Aufrufer darf das grundsätzlich, nur diese
            // Anzeige ist gerade nicht in dem Zustand.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status409Conflict, "Request failed", fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status422UnprocessableEntity,
                "Request failed", fehler.Message);
        }

        _ = abbruch;
    }

    private static bool Firma(ICurrentPrincipal akteur, out TenantId firma)
    {
        if (akteur.Current?.Acting is Capacity.ForCompany fuer)
        {
            firma = fuer.Tenant;
            return true;
        }

        firma = TenantId.None;
        return false;
    }

    private static Task KeineFirma(HttpContext context, ICurrentPrincipal akteur) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            : ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden, "Request failed", "no active company");

    private static Stellenangaben Angaben(StelleSchreibenV1 koerper) => new(
        koerper.Title, koerper.Description, koerper.Location, koerper.PostalCode,
        Grad(koerper.RemoteMode) ?? Remotegrad.None,
        Anstellung(koerper.EmploymentType),
        koerper.Skills ?? []);

    private static Remotegrad? Grad(string? roh) => roh?.Trim().ToLowerInvariant() switch
    {
        "none" => Remotegrad.None,
        "hybrid" => Remotegrad.Hybrid,
        "full" => Remotegrad.Full,
        _ => null
    };

    /// <summary>Eine Zahl aus der Abfrage, oder <c>null</c>.</summary>
    /// <remarks>
    /// <c>InvariantCulture</c> ist nicht kosmetisch: eine Adresszeile trägt
    /// <c>52.52</c>, und ein Server mit deutschem Gebietsschema läse den Punkt
    /// ohne diese Angabe als Tausendertrennzeichen — aus 52.52 würde 5252, und
    /// die Umkreissuche zeigte nichts, ohne einen Fehler zu melden.
    /// </remarks>
    private static double? Zahl(string? roh) =>
        double.TryParse(
            roh, NumberStyles.Float, CultureInfo.InvariantCulture, out var wert)
            ? wert
            : null;

    private static Anstellungsart Anstellung(string? roh) => roh?.Trim().ToLowerInvariant() switch
    {
        "part_time" => Anstellungsart.PartTime,
        "contract" => Anstellungsart.Contract,
        "internship" => Anstellungsart.Internship,
        _ => Anstellungsart.FullTime
    };

    private static StelleV1 Antwort(Stelle stelle) => new(
        stelle.Id,
        stelle.Firma.Value,
        stelle.Titel,
        stelle.Beschreibung,
        stelle.Ort,
        stelle.Postleitzahl,
        stelle.Remote.ToString().ToLowerInvariant(),
        stelle.Art switch
        {
            Anstellungsart.PartTime => "part_time",
            Anstellungsart.Contract => "contract",
            Anstellungsart.Internship => "internship",
            _ => "full_time"
        },
        stelle.Faehigkeiten.Werte,
        stelle.Stand.ToString().ToLowerInvariant(),
        stelle.VeroeffentlichtAm,
        stelle.GeaendertAm);

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
