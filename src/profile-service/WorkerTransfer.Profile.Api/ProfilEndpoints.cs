using System.Text.Json.Serialization;
using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Entwurf;
using WorkerTransfer.Profile.Application.Loeschung;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Domain;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>Was jemand schickt, um sein Profil zu schreiben.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen Profil das ist, steht im geprüften Token.
/// Und <b>ohne jedes Sichtbarkeitsfeld</b> — ob ein Profil gezeigt werden darf,
/// steht ausschließlich im Consent-Ledger (ADR-0020). Ein Feld hier wäre eine
/// zweite Wahrheit, und die beiden wären beim ersten Widerruf uneins.
/// </remarks>
/// <remarks>
/// <para><strong><c>remote_ok</c> steht ausdrücklich da, und hier war der Schaden
/// still.</strong> Ohne <c>[JsonPropertyName]</c> band <c>RemoteOk</c> auf
/// <c>remoteOk</c>, während die Oberfläche <c>remote_ok</c> schickt
/// (<c>apps/web/src/profile/client.ts:98</c>). Ein <c>bool</c> hat keine
/// Not-Null-Sperre und keinen Wächter: er fiel einfach auf <c>false</c>
/// zurück.</para>
///
/// <para>Wer „Remote möglich" ankreuzte und speicherte, bekam <c>200</c> — und
/// das Häkchen war weg. Das ist die unangenehmere Hälfte desselben Fehlers:
/// die Registrierung schlug wenigstens laut fehl.</para>
/// </remarks>
public sealed record ProfilKoerper(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("bio")] string Bio,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_ok")] bool RemoteOk,
    [property: JsonPropertyName("skills")] IReadOnlyList<string> Skills,
    /// <summary>Eine Stufe, oder <c>null</c> für „nichts gesagt" (ADR-0041).</summary>
    /// <remarks>
    /// <c>null</c> ist ein gültiger Wert und bedeutet etwas: die Angabe wird
    /// damit <em>zurückgenommen</em>. Ein „fehlt heisst: lass wie es war" machte
    /// die Rücknahme unmöglich — und eine Aussage, die man nicht zurücknehmen
    /// kann, ist keine freiwillige.
    /// </remarks>
    [property: JsonPropertyName("commute_km")] string? CommuteKm = null,
    [property: JsonPropertyName("relocation")] string? Relocation = null);

/// <summary>Was jemand schickt, um sich beim Formulieren helfen zu lassen.</summary>
public sealed record EntwurfKoerper(string Wish);

/// <summary><c>/profiles</c> — und seit dem 11.09.2026 nichts mehr daneben.</summary>
/// <remarks>
/// <c>GET /candidates</c> stand hier und ist gefallen: scout-service ist sein
/// Nachfolger, und zwei Suchen nebeneinander waeren zwei Wahrheiten
/// (ADR-0036 Entscheidung 1). Was von ihm gebraucht wird, liegt jetzt hinter
/// <c>/internal/profiles/search</c> — hinter dem geteilten Geheimnis, ohne
/// Gateway-Route, und ohne Ledgerpruefung, weil die eine Ebene hoeher steht.
/// </remarks>
public static class ProfilEndpoints
{
    /// <summary>Bindet die Profilrouten ein.</summary>
    public static IEndpointRouteBuilder MapProfilEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ein schweigender Ledger ist weder ein Ja noch ein Nein. 404 sagte,
        // die Person sei nicht da; das Profil auszugeben hieße, etwas
        // herzugeben, das niemand freigegeben hat. 503 sagt das einzig Wahre:
        // dieser Dienst kann gerade nicht antworten.
        //
        // Hier als ein Filter statt als Fang in jeder Route, weil die Route,
        // die ihn vergisst, wie eine funktionierende aussieht, bis der Ledger
        // ausfällt.
        var profile = app.MapGroup("/profiles").AddEndpointFilter(SchweigenAbfangen);

        profile.MapPut("/me", async (
            ProfilKoerper koerper,
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

            try
            {
                var profil = await mediator.Send(
                    new ProfilSpeichernBefehl(
                        handelnder.Subject, koerper.Headline, koerper.Bio,
                        koerper.Location, koerper.RemoteOk, koerper.Skills ?? [],
                        Pendelstufen.Lies(koerper.CommuteKm),
                        Umzugsworte.Lies(koerper.Relocation)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(profil), cancellationToken);
            }
            catch (Eingabefehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        profile.MapGet("/me", async (
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

            // Das eigene Profil ohne Ledgerfrage: die Freigabe regelt, wer es
            // von aussen sieht, nicht ob jemand sein eigenes lesen darf.
            var profil = await mediator.Send(
                new MeinProfilAbfrage(handelnder.Subject), cancellationToken);

            // 200 null, nicht 404: „noch keins" ist der Normalfall, und der
            // Browser macht aus jedem 404 eine Konsolezeile.
            await context.Response.WriteAsJsonAsync(
                profil is null ? null : Antwort(profil), cancellationToken);
        });

        profile.MapPost("/me/draft", async (
            EntwurfKoerper koerper,
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

            try
            {
                // Der Entwurf lebt im Formular, bis die Person ihn speichert —
                // und dann ist es ihr Text. Nichts davon wird hier abgelegt.
                var text = await mediator.Send(
                    new EntwurfAbfrage(handelnder.Subject, koerper.Wish ?? string.Empty),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (EntwurfNichtVerfuegbar fehler)
            {
                // Die Art des Fehlschlags, nie sein Inhalt — und ausdrücklich
                // keine Vorlage, die wie ein Vorschlag aussähe.
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

        profile.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                // 403 ist eine Aussage über den Aufrufer und verrät nichts über
                // die Person, nach der er fragt.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var profil = await mediator.Send(
                new FremdesProfilAbfrage(new SubjectId(subjectId), firma.Tenant),
                cancellationToken);

            if (profil is null)
            {
                // Verborgen und nicht vorhanden antworten gleich, und das muss
                // so bleiben: ein Unterschied sagte, ob dieser Mensch hier ist.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(profil), cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.
    /// </summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// Anfrage ohne Rumpf sieht der Aufrufer trotzdem sein 503 und nur das
    /// Protokoll trägt eine unbehandelte Ausnahme; bei einer mit Rumpf reißt
    /// die Verbindung, und er bekommt „Error while copying content to a
    /// stream". Gemessen an <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    private static async ValueTask<object?> SchweigenAbfangen(
        EndpointFilterInvocationContext aufruf, EndpointFilterDelegate weiter)
    {
        try
        {
            return await weiter(aufruf);
        }
        catch (EinwilligungSchweigt)
        {
            await ProblemDetailsMiddleware.Schreibe(
                aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the consent ledger did not answer");

            return Geschrieben;
        }
    }

    /// <remarks>
    /// Trägt kein Sichtbarkeitsfeld, keinen Punktwert, keinen Rang und keinen
    /// Prozentwert — und es gehört keines darauf (ADR-0022).
    /// </remarks>
    private static Dictionary<string, object?> Antwort(Profil profil) => new()
    {
        ["subject_id"] = profil.Wer.Value,
        ["headline"] = profil.Ueberschrift,
        ["bio"] = profil.Text,
        ["location"] = profil.Ort,
        ["remote_ok"] = profil.RemoteMoeglich,
        ["skills"] = profil.Faehigkeiten.Werte,
        // `null` reist mit und wird nicht weggelassen: die Oberflaeche muss
        // „nichts gesagt" von „Feld gibt es nicht" unterscheiden koennen.
        ["commute_km"] = Pendelstufen.Wort(profil.Pendelbereitschaft),
        ["relocation"] = Umzugsworte.Wort(profil.Umzugsbereitschaft),
        ["updated_at"] = profil.GeaendertAm
    };

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
