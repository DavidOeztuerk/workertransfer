using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Profile.Application.Entwurf;
using WorkerTransfer.Profile.Application.Erasure;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Domain;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>Was jemand schickt, um sein PersonProfile zu schreiben.</summary>
/// <remarks>
/// Ohne <c>subject_id</c>: wessen PersonProfile das ist, steht im geprüften Token.
/// Und <b>ohne jedes Sichtbarkeitsfeld</b> — ob ein PersonProfile gezeigt werden darf,
/// steht ausschließlich im Consent-Ledger (ADR-0020). Ein Feld hier wäre eine
/// zweite Wahrheit, und die beiden wären beim ersten Revocation uneins.
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
public sealed record ProfileBody(
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
public sealed record DraftBody(string Wish);

/// <summary><c>/profiles</c> — und seit dem 11.09.2026 nichts mehr daneben.</summary>
/// <remarks>
/// <c>GET /candidates</c> stand hier und ist gefallen: scout-service ist sein
/// Nachfolger, und zwei Searches nebeneinander waeren zwei Wahrheiten
/// (ADR-0036 Entscheidung 1). Was von ihm gebraucht wird, liegt jetzt hinter
/// <c>/internal/profiles/search</c> — hinter dem geteilten Geheimnis, ohne
/// Gateway-Route, und ohne LedgerCheck, weil die eine Ebene hoeher steht.
/// </remarks>
public static class ProfileEndpoints
{
    /// <summary>Bindet die Profilrouten ein.</summary>
    public static IEndpointRouteBuilder MapProfilEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ein schweigender Ledger ist weder ein Ja noch ein Nein. 404 sagte,
        // die Person sei nicht da; das PersonProfile auszugeben hieße, etwas
        // herzugeben, das niemand freigegeben hat. 503 sagt das einzig Wahre:
        // dieser Dienst kann gerade nicht antworten.
        //
        // Hier als ein Filter statt als Fang in jeder Route, weil die Route,
        // die ihn vergisst, wie eine funktionierende aussieht, bis der Ledger
        // ausfällt.
        var profile = app.MapGroup("/profiles").AddEndpointFilter(SchweigenAbfangen);

        profile.MapPut("/me", async (
            ProfileBody koerper,
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
                var profile = await mediator.Send(
                    new StoreProfileCommand(
                        handelnder.Subject, koerper.Headline, koerper.Bio,
                        koerper.Location, koerper.RemoteOk, koerper.Skills ?? [],
                        Pendelstufen.Lies(koerper.CommuteKm),
                        RelocationTerms.Lies(koerper.Relocation)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(profile), cancellationToken);
            }
            catch (Eingabefehler error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
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

            // Das eigene PersonProfile ohne Ledgerfrage: die Release regelt, wer es
            // von aussen sieht, nicht ob jemand sein eigenes lesen darf.
            var profile = await mediator.Send(
                new MyProfileQuery(handelnder.Subject), cancellationToken);

            // 200 null, nicht 404: „noch keins" ist der Normalfall, und der
            // Browser macht aus jedem 404 eine Konsolezeile.
            await context.Response.WriteAsJsonAsync(
                profile is null ? null : Antwort(profile), cancellationToken);
        });

        profile.MapPost("/me/draft", async (
            DraftBody koerper,
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
                    new DraftQuery(handelnder.Subject, koerper.Wish ?? string.Empty),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (DraftUnavailable error)
            {
                // Die Art des Fehlschlags, nie sein Inhalt — und ausdrücklich
                // keine ResumeTemplate, die wie ein Vorschlag aussähe.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", error.Message);
            }
            catch (Eingabefehler error)
            {
                // Ein zu langer Wunsch ist eine Eingabe, kein Ausfall: 422, nicht
                // 503. Ohne diesen Zweig verliesse er den Dienst als 500 — und
                // ein 500 sagt dem Aufrufer, es liege nicht an ihm.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
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

            if (handelnder.Acting is not Capacity.ForCompany company)
            {
                // 403 ist eine Aussage über den Aufrufer und verrät nichts über
                // die Person, nach der er fragt.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var profile = await mediator.Send(
                new ForeignProfileQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            if (profile is null)
            {
                // Verborgen und nicht vorhanden antworten gleich, und das muss
                // so bleiben: ein Unterschied sagte, ob dieser Mensch hier ist.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(profile), cancellationToken);
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
    /// ResumeRequest ohne Rumpf sieht der Aufrufer trotzdem sein 503 und nur das
    /// Protokoll trägt eine unbehandelte Ausnahme; bei einer mit Rumpf reißt
    /// die Connection, und er bekommt „Error while copying content to a
    /// stream". Gemessen an <c>POST /applications</c>.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    private static async ValueTask<object?> SchweigenAbfangen(
        EndpointFilterInvocationContext call, EndpointFilterDelegate weiter)
    {
        try
        {
            return await weiter(call);
        }
        catch (ConsentGateSilent)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the consent ledger did not answer");

            return Geschrieben;
        }
    }

    /// <remarks>
    /// Trägt kein Sichtbarkeitsfeld, keinen Punktwert, keinen Rang und keinen
    /// Prozentwert — und es gehört keines darauf (ADR-0022).
    /// </remarks>
    private static Dictionary<string, object?> Antwort(PersonProfile profile) => new()
    {
        ["subject_id"] = profile.Wer.Value,
        ["headline"] = profile.Ueberschrift,
        ["bio"] = profile.Text,
        ["location"] = profile.Ort,
        ["remote_ok"] = profile.RemoteMoeglich,
        ["skills"] = profile.Faehigkeiten.Werte,
        // `null` reist mit und wird nicht weggelassen: die Oberflaeche muss
        // „nichts gesagt" von „Feld gibt es nicht" unterscheiden koennen.
        ["commute_km"] = Pendelstufen.Wort(profile.Pendelbereitschaft),
        ["relocation"] = RelocationTerms.Wort(profile.Umzugsbereitschaft),
        ["updated_at"] = profile.GeaendertAm
    };

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
