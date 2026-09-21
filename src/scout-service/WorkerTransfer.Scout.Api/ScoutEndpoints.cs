using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Ansprache;
using WorkerTransfer.Scout.Application.Kandidaten;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Application.Searches;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.Scout.Domain.Hit;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Api;

/// <summary><c>/scout</c> — suchen, ablegen, ansprechen.</summary>
/// <remarks>
/// <para><strong>Firmenzwang überall.</strong> Wer für sich selbst handelt,
/// bekommt 403 „no active company" — eine Aussage über den <em>Aufrufer</em>,
/// die über die Gesuchten nichts verrät. Das ist der harte Teil aus
/// <c>GET /candidates</c>, mitgenommen und nicht neu erfunden.</para>
///
/// <para><strong>Und <c>member</c> genügt, kein <c>admin</c>.</strong> Searches,
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
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var request = context.Request.Query;

            SearchFilter filter;

            try
            {
                filter = SearchFilter.Aus(
                    request["skill"].Count > 0 ? [.. request["skill"]!] : null,
                    request["location"].ToString(),
                    request["remote"] == "true");
            }
            catch (Eingabefehler error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
                return;
            }

            // `stelle` FILTERT NICHT. Sie fuegt jedem Hit eine Auskunft
            // hinzu und nimmt keinen weg (ADR-0041): wer weiter weg wohnt,
            // bleibt in der Liste und traegt ein Kreuz. Das Unternehmen
            // entscheidet, nicht die Search — und es sieht, warum.
            var page = await mediator.Send(
                new KandidatenAbfrage(
                    company, filter, Laenge(request["limit"]), request["cursor"],
                    Guid.TryParse(request["stelle"], out var welche) ? welche : null),
                cancellationToken);

            // DIE EINE STELLE, AN DER EIN LESEN ETWAS SCHREIBT — und sie steht
            // hier absichtlich sichtbar statt versteckt in der Abfrage.
            //
            // Wer in einer Search auftaucht, ist entdeckt worden, und ADR-0033
            // sagt: das erfaehrt die Person. Nicht ueber ein Protokoll „wer hat
            // wen angesehen" — das waere die sensibelste Tabelle des Systems —
            // sondern ueber eine Nachricht, die KEIN Unternehmen nennt.
            //
            // Als eigener BEFEHL und nicht in der Abfrage, weil nur ein Befehl
            // eine Transaktionsklammer bekommt: die Ausgangszeile muss mit
            // ihrem Anlass gemeinsam festgeschrieben werden oder gar nicht
            // (ADR-0025).
            if (page.Eintraege.Count > 0)
            {
                await mediator.Send(
                    new EntdeckungMeldenBefehl([.. page.Eintraege.Select(t => t.Wer)]),
                    cancellationToken);
            }

            // None Gesamtzahl (ADR-0026): sie verriete ueber die Differenz zur
            // Seitenlaenge, wie viele Profile NICHT freigegeben sind.
            await context.Response.WriteAsJsonAsync(
                new HitPageV1([.. page.Eintraege.Select(Antwort)], page.Weiter),
                cancellationToken);
        });

        scout.MapPost("/candidates/{subjectId:guid}/draft", async (
            Guid subjectId,
            ApproachRequestV1? koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                // Die gesuchten Worte laufen durch denselben Filter wie die
                // Search: derselbe Vocabulary, dieselben Grenzen — und damit
                // auch hier keine Moeglichkeit, einen Evidence unterzuschieben.
                var gesucht = SearchFilter.Aus(koerper?.Skills, null, false);

                var draft = await mediator.Send(
                    new AnspracheAbfrage(
                        company,
                        new SubjectId(subjectId),
                        gesucht.GenannteWorte,
                        koerper?.Wish ?? string.Empty),
                    cancellationToken);

                // Nur der Text. Kein `sent_at`, kein Empfaenger, keine Kennung
                // eines Vorgangs: es gibt keinen Case, weil nichts gesendet
                // wurde (ADR-0036 Auflage 4).
                await context.Response.WriteAsJsonAsync(
                    new ApproachDraftV1(draft), cancellationToken);
            }
            catch (NoContactPerson error)
            {
                // Verborgen und nicht vorhanden antworten gleich, und das muss
                // so bleiben: ein Unterschied sagte, ob dieser Mensch hier ist.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", error.Message);
            }
            catch (AnspracheNichtVerfuegbar error)
            {
                // Die Art des Fehlschlags, nie sein Inhalt — und ausdruecklich
                // keine ResumeTemplate, die wie ein Vorschlag aussaehe.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", error.Message);
            }
            catch (Eingabefehler error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        scout.MapPost("/searches", async (
            CreateSearchV1? koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                var filter = SearchFilter.Aus(
                    koerper?.Skills, koerper?.Location, koerper?.Remote ?? false);

                var search = await mediator.Send(
                    new SaveSearchCommand(
                        company, akteur.Current!.Subject, koerper?.Name ?? string.Empty, filter),
                    cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(Antwort(search), cancellationToken);
            }
            catch (Eingabefehler error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        });

        scout.MapGet("/searches", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var searches = await mediator.Send(
                new MySearchesQuery(company, akteur.Current!.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, object?>
                {
                    ["items"] = searches.Select(Antwort).ToArray()
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
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var weg = await mediator.Send(
                new DeleteSearchCommand(id, company, akteur.Current!.Subject), cancellationToken);

            if (!weg)
            {
                // 404 auch fuer die Search eines Kollegen: ein Unterschied
                // verriete, dass sie existiert.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such search");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        return app;
    }

    /// <summary>Das Unternehmen aus dem Token — nie aus der ResumeRequest.</summary>
    /// <remarks>
    /// Der Mandant kommt aus dem geprüften Token (ADR-0017/0018). Ihn aus einem
    /// Header oder einem Feld zu nehmen hiesse, den Aufrufer entscheiden zu
    /// lassen, in wessen Namen er sucht.
    /// </remarks>
    private static TenantId? Firma(ICurrentPrincipal akteur) =>
        akteur.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    private static Task OhneFirma(ICurrentPrincipal akteur, HttpContext context) =>
        akteur.Current is null
            ? ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated")
            // 403 ist eine Aussage ueber den Aufrufer und verraet nichts ueber
            // die Menschen, nach denen er fragt.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    /// <summary>
    /// Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.
    /// </summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// ResumeRequest mit Rumpf reisst dann die Connection.
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
        catch (ProfileSearchSilent)
        {
            // Ausdruecklich kein leeres Ergebnis: eine leere Trefferliste saehe
            // aus wie „es gibt niemanden", und das waere eine Aussage ueber
            // Menschen, die aus unserem Ausfall stammt.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", "the profile search did not answer");

            return Geschrieben;
        }
    }

    /// <summary>Wie viele Zeilen eine Seite trägt.</summary>
    private static int Laenge(string? roh) =>
        int.TryParse(roh, out var value)
        && value > 0
        && value <= KandidatenHandler.Hoechstlaenge
            ? value
            : KandidatenHandler.Vorgabe;

    /// <remarks>
    /// Trägt keinen Punktwert, keinen Rang und keinen Prozentwert — und es
    /// gehört keiner darauf (ADR-0022). Was hier steht, hat die Person
    /// geschrieben oder ist ein Evidence mit ProviderOrigin und Link.
    /// </remarks>
    private static HitV1 Antwort(Hit hit) =>
        new(
            hit.Wer.Value,
            hit.Ueberschrift,
            hit.Text,
            hit.Ort,
            hit.RemoteMoeglich,
            hit.Genannt,
            [.. hit.Haken.Select(haken => new HakenV1(haken.Wort, haken.Genannt))],
            [
                .. hit.Belege.Select(evidence => new EvidenceV1(
                    evidence.Wort, Wort(evidence.Art), evidence.Projekt, evidence.Adresse))
            ],
            Wort(hit.EvidenceState),
            AvailabilityTerms.Wort(hit.Availability.State),
            AvailabilityTerms.Wort(hit.Availability.Stufe),
            AvailabilityTerms.Wort(hit.Availability.Anwesenheit));

    private static SearchV1 Antwort(Search search) =>
        new(
            search.Id,
            search.Name,
            search.Filter.GenannteWorte,
            search.Filter.Ort,
            search.Filter.NurRemote,
            search.AngelegtAm);

    /// <summary>Das Wort zur EvidenceKind, auf dem Draht.</summary>
    private static string Wort(EvidenceKind kind) => kind switch
    {
        EvidenceKind.Thema => "topic",
        EvidenceKind.Sprache => "language",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    /// <summary>Das Wort zum EvidenceState, auf dem Draht.</summary>
    /// <remarks>
    /// Ein Wort und kein Satz: die Oberfläche formuliert in der Sprache der
    /// lesenden Person (ADR-0031), und ein deutscher Satz auf dem Draht wäre
    /// eine Sprache, die der Server für alle festlegt.
    /// </remarks>
    private static string Wort(EvidenceState state) => state switch
    {
        EvidenceState.Vollstaendig => "complete",
        EvidenceState.KeineFreigegeben => "none_released",
        EvidenceState.Unvollstaendig => "partial",
        EvidenceState.Unerreichbar => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
