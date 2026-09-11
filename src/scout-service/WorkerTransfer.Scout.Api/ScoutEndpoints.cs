using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Ansprache;
using WorkerTransfer.Scout.Application.Kandidaten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Application.Suchen;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Suchen;
using WorkerTransfer.Scout.Domain.Treffer;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Api;

/// <summary><c>/scout</c> — suchen, ablegen, ansprechen.</summary>
/// <remarks>
/// <para><strong>Firmenzwang überall.</strong> Wer für sich selbst handelt,
/// bekommt 403 „no active company" — eine Aussage über den <em>Aufrufer</em>,
/// die über die Gesuchten nichts verrät. Das ist der harte Teil aus
/// <c>GET /candidates</c>, mitgenommen und nicht neu erfunden.</para>
///
/// <para><strong>Und <c>member</c> genügt, kein <c>admin</c>.</strong> Suchen,
/// entwerfen und ansprechen ist die tägliche Arbeit im Namen der Firma; ein zu
/// enges Recht machte aus einer Einladung eine Zuschauerkarte, und dann legt
/// jemand einen zweiten Admin an, um arbeiten zu können.</para>
/// </remarks>
public static class ScoutEndpoints
{
    /// <summary>Bindet die Scout-Routen ein.</summary>
    public static IEndpointRouteBuilder MapScoutEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ein schweigender Ledger ist weder ein Ja noch ein Nein, und eine
        // schweigende Profilsuche auch nicht. 404 sagte, es gebe niemanden; eine
        // leere Liste behauptete dasselbe leiser. 503 sagt das einzig Wahre:
        // dieser Dienst kann gerade nicht antworten.
        //
        // Als ein Filter statt als Fang in jeder Route, weil die Route, die ihn
        // vergisst, wie eine funktionierende aussieht, bis etwas ausfällt.
        var scout = app.MapGroup("/scout").AddEndpointFilter(SchweigenAbfangen);

        scout.MapGet("/candidates", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } firma)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var anfrage = context.Request.Query;

            Suchfilter filter;

            try
            {
                filter = Suchfilter.Aus(
                    anfrage["skill"].Count > 0 ? [.. anfrage["skill"]!] : null,
                    anfrage["location"].ToString(),
                    anfrage["remote"] == "true");
            }
            catch (Eingabefehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
                return;
            }

            // `stelle` FILTERT NICHT. Sie fuegt jedem Treffer eine Auskunft
            // hinzu und nimmt keinen weg (ADR-0041): wer weiter weg wohnt,
            // bleibt in der Liste und traegt ein Kreuz. Das Unternehmen
            // entscheidet, nicht die Suche — und es sieht, warum.
            var seite = await mediator.Send(
                new KandidatenAbfrage(
                    firma, filter, Laenge(anfrage["limit"]), anfrage["cursor"],
                    Guid.TryParse(anfrage["stelle"], out var welche) ? welche : null),
                cancellationToken);

            // DIE EINE STELLE, AN DER EIN LESEN ETWAS SCHREIBT — und sie steht
            // hier absichtlich sichtbar statt versteckt in der Abfrage.
            //
            // Wer in einer Suche auftaucht, ist entdeckt worden, und ADR-0033
            // sagt: das erfaehrt die Person. Nicht ueber ein Protokoll „wer hat
            // wen angesehen" — das waere die sensibelste Tabelle des Systems —
            // sondern ueber eine Nachricht, die KEIN Unternehmen nennt.
            //
            // Als eigener BEFEHL und nicht in der Abfrage, weil nur ein Befehl
            // eine Transaktionsklammer bekommt: die Ausgangszeile muss mit
            // ihrem Anlass gemeinsam festgeschrieben werden oder gar nicht
            // (ADR-0025).
            if (seite.Eintraege.Count > 0)
            {
                await mediator.Send(
                    new EntdeckungMeldenBefehl([.. seite.Eintraege.Select(t => t.Wer)]),
                    cancellationToken);
            }

            // Keine Gesamtzahl (ADR-0026): sie verriete ueber die Differenz zur
            // Seitenlaenge, wie viele Profile NICHT freigegeben sind.
            await context.Response.WriteAsJsonAsync(
                new TrefferseiteV1([.. seite.Eintraege.Select(Antwort)], seite.Weiter),
                cancellationToken);
        });

        scout.MapPost("/candidates/{subjectId:guid}/draft", async (
            Guid subjectId,
            AnspracheAnfrageV1? koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } firma)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                // Die gesuchten Worte laufen durch denselben Filter wie die
                // Suche: derselbe Wortschatz, dieselben Grenzen — und damit
                // auch hier keine Moeglichkeit, einen Beleg unterzuschieben.
                var gesucht = Suchfilter.Aus(koerper?.Skills, null, false);

                var entwurf = await mediator.Send(
                    new AnspracheAbfrage(
                        firma,
                        new SubjectId(subjectId),
                        gesucht.GenannteWorte,
                        koerper?.Wish ?? string.Empty),
                    cancellationToken);

                // Nur der Text. Kein `sent_at`, kein Empfaenger, keine Kennung
                // eines Vorgangs: es gibt keinen Vorgang, weil nichts gesendet
                // wurde (ADR-0036 Auflage 4).
                await context.Response.WriteAsJsonAsync(
                    new AnspracheentwurfV1(entwurf), cancellationToken);
            }
            catch (KeinAnsprechpartner fehler)
            {
                // Verborgen und nicht vorhanden antworten gleich, und das muss
                // so bleiben: ein Unterschied sagte, ob dieser Mensch hier ist.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", fehler.Message);
            }
            catch (AnspracheNichtVerfuegbar fehler)
            {
                // Die Art des Fehlschlags, nie sein Inhalt — und ausdruecklich
                // keine Vorlage, die wie ein Vorschlag aussaehe.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", fehler.Message);
            }
            catch (Eingabefehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        scout.MapPost("/searches", async (
            SucheAnlegenV1? koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } firma)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                var filter = Suchfilter.Aus(
                    koerper?.Skills, koerper?.Location, koerper?.Remote ?? false);

                var suche = await mediator.Send(
                    new SucheSpeichernBefehl(
                        firma, akteur.Current!.Subject, koerper?.Name ?? string.Empty, filter),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(Antwort(suche), cancellationToken);
            }
            catch (Eingabefehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        scout.MapGet("/searches", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } firma)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var suchen = await mediator.Send(
                new MeineSuchenAbfrage(firma, akteur.Current!.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["items"] = suchen.Select(Antwort).ToArray()
                },
                cancellationToken);
        });

        scout.MapDelete("/searches/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } firma)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var weg = await mediator.Send(
                new SucheLoeschenBefehl(id, firma, akteur.Current!.Subject), cancellationToken);

            if (!weg)
            {
                // 404 auch fuer die Suche eines Kollegen: ein Unterschied
                // verriete, dass sie existiert.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such search");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    /// <summary>Das Unternehmen aus dem Token — nie aus der Anfrage.</summary>
    /// <remarks>
    /// Der Mandant kommt aus dem geprüften Token (ADR-0017/0018). Ihn aus einem
    /// Kopf oder einem Feld zu nehmen hiesse, den Aufrufer entscheiden zu
    /// lassen, in wessen Namen er sucht.
    /// </remarks>
    private static TenantId? Firma(ICurrentPrincipal akteur) =>
        akteur.Current?.Acting is Capacity.ForCompany firma ? firma.Tenant : null;

    private static Task OhneFirma(ICurrentPrincipal akteur, HttpContext context) =>
        akteur.Current is null
            ? ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated")
            // 403 ist eine Aussage ueber den Aufrufer und verraet nichts ueber
            // die Menschen, nach denen er fragt.
            : ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    /// <summary>
    /// Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.
    /// </summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// Anfrage mit Rumpf reisst dann die Verbindung.
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
        catch (ProfilsucheSchweigt)
        {
            // Ausdruecklich kein leeres Ergebnis: eine leere Trefferliste saehe
            // aus wie „es gibt niemanden", und das waere eine Aussage ueber
            // Menschen, die aus unserem Ausfall stammt.
            await ProblemDetailsMiddleware.Schreibe(
                aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the profile search did not answer");

            return Geschrieben;
        }
    }

    /// <summary>Wie viele Zeilen eine Seite trägt.</summary>
    private static int Laenge(string? roh) =>
        int.TryParse(roh, out var wert)
        && wert > 0
        && wert <= KandidatenHandler.Hoechstlaenge
            ? wert
            : KandidatenHandler.Vorgabe;

    /// <remarks>
    /// Trägt keinen Punktwert, keinen Rang und keinen Prozentwert — und es
    /// gehört keiner darauf (ADR-0022). Was hier steht, hat die Person
    /// geschrieben oder ist ein Beleg mit Herkunft und Link.
    /// </remarks>
    private static TrefferV1 Antwort(Treffer treffer) =>
        new(
            treffer.Wer.Value,
            treffer.Ueberschrift,
            treffer.Text,
            treffer.Ort,
            treffer.RemoteMoeglich,
            treffer.Genannt,
            [.. treffer.Haken.Select(haken => new HakenV1(haken.Wort, haken.Genannt))],
            [
                .. treffer.Belege.Select(beleg => new BelegV1(
                    beleg.Wort, Wort(beleg.Art), beleg.Projekt, beleg.Adresse))
            ],
            Wort(treffer.Belegstand),
            Erreichbarkeitsworte.Wort(treffer.Erreichbarkeit.Stand),
            Erreichbarkeitsworte.Wort(treffer.Erreichbarkeit.Stufe),
            Erreichbarkeitsworte.Wort(treffer.Erreichbarkeit.Anwesenheit));

    private static SucheV1 Antwort(Suche suche) =>
        new(
            suche.Id,
            suche.Name,
            suche.Filter.GenannteWorte,
            suche.Filter.Ort,
            suche.Filter.NurRemote,
            suche.AngelegtAm);

    /// <summary>Das Wort zur Belegart, auf dem Draht.</summary>
    private static string Wort(Belegart art) => art switch
    {
        Belegart.Thema => "topic",
        Belegart.Sprache => "language",
        _ => throw new ArgumentOutOfRangeException(nameof(art))
    };

    /// <summary>Das Wort zum Belegstand, auf dem Draht.</summary>
    /// <remarks>
    /// Ein Wort und kein Satz: die Oberfläche formuliert in der Sprache der
    /// lesenden Person (ADR-0031), und ein deutscher Satz auf dem Draht wäre
    /// eine Sprache, die der Server für alle festlegt.
    /// </remarks>
    private static string Wort(Belegstand stand) => stand switch
    {
        Belegstand.Vollstaendig => "complete",
        Belegstand.KeineFreigegeben => "none_released",
        Belegstand.Unvollstaendig => "partial",
        Belegstand.Unerreichbar => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };
}
