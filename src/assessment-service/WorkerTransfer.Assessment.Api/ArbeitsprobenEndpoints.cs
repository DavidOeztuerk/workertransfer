using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Vorgaenge;
using WorkerTransfer.Assessment.Contracts;
using WorkerTransfer.Assessment.Domain.Vorgaenge;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Api;

/// <summary><c>/assessments</c> — die Aufgabe, die Lösung, die Rückmeldung.</summary>
/// <remarks>
/// <para><strong>Sechs Routen, und die Menge ist geschlossen.</strong>
/// <c>AuflagenTests</c> schreibt sie namentlich auf; wer eine siebte hinzufügt,
/// ändert diesen Test und beantwortet dabei die Frage, ob sie eine Aussage über
/// einen Menschen herausgibt (ADR-0042 §1).</para>
///
/// <para><strong>Und eine, die es ausdrücklich nicht gibt: Ablehnen.</strong>
/// Keine Route, kein Zustand, kein Feld. Wer eine Aufgabe nicht will, tut
/// nichts; die Frist läuft ab, und das ist ununterscheidbar von „hat es nicht
/// geschafft". Ein höflicher Absageknopf wäre freundlicher zum Unternehmen und
/// erzeugte genau den Vermerk, den ADR-0042 §3 verbietet — „hat dreimal
/// abgelehnt" ist eine Tatsache über einen Menschen, sie entsteht aus lauter
/// einzelnen berechtigten Klicks, und sie ist danach da.</para>
///
/// <para><strong><c>member</c> genügt, kein <c>admin</c>.</strong> Eine
/// Arbeitsprobe zu stellen und zu beantworten ist die tägliche Arbeit im Namen
/// der Firma; sie bindet das Unternehmen nicht. Ein zweites Recht davor machte
/// aus einer Einladung eine Zuschauerkarte.</para>
/// </remarks>
public static class ArbeitsprobenEndpoints
{
    /// <summary>Bindet die Routen ein.</summary>
    public static IEndpointRouteBuilder MapArbeitsprobenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Als ein Filter statt als Fang in jeder Route, weil die Route, die ihn
        // vergisst, wie eine funktionierende aussieht, bis etwas ausfaellt.
        var proben = app.MapGroup("/assessments").AddEndpointFilter(Abfangen);

        MapFirmenseite(proben);
        MapPersonenseite(proben);
        MapGemeinsam(proben);

        return app;
    }

    // -----------------------------------------------------------------------
    // Die Seite des Unternehmens
    // -----------------------------------------------------------------------

    private static void MapFirmenseite(IEndpointRouteBuilder proben)
    {
        proben.MapPost("/", async (
            AufgabeStellenV1? koerper,
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

            if (koerper is null || koerper.SubjectId == Guid.Empty)
            {
                await Unbrauchbar(context, "subject_id is required");
                return;
            }

            // Umfang und Frist sind Pflicht, und die Ablehnung sagt das, statt
            // sich eine Vorgabe auszudenken: eine erfundene Zahl haelte die
            // Person fuer eine Angabe des Unternehmens (ADR-0042 §3).
            if (koerper.Hours is not { } stunden)
            {
                await Unbrauchbar(context, "hours is required — the effort is stated up front");
                return;
            }

            if (koerper.DueAt is not { } frist)
            {
                await Unbrauchbar(context, "due_at is required");
                return;
            }

            var vorgang = await mediator.Send(
                new AufgabeStellenBefehl(
                    new SubjectId(koerper.SubjectId),
                    firma,
                    koerper.Title,
                    koerper.Task,
                    stunden,
                    frist),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;

            await context.Response.WriteAsJsonAsync(Sicht(vorgang, context), cancellationToken);
        });

        proben.MapGet("/", async (
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

            var unsere = await mediator.Send(
                new FirmenvorgaengeAbfrage(firma), cancellationToken);

            // Keine Gesamtzahl (ADR-0026): sie verriete ueber die Differenz zur
            // Laenge, wie viele Menschen dem Unternehmen die Sicht entzogen
            // haben.
            await context.Response.WriteAsJsonAsync(
                new VorgangslisteV1([.. unsere.Select(vorgang => Sicht(vorgang, context))]),
                cancellationToken);
        });

        proben.MapPost("/{id:guid}/evaluation", async (
            Guid id,
            BewertenV1? koerper,
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

            if (Woerter.LiesAusgang(koerper?.Outcome) is not { } ausgang)
            {
                await Unbrauchbar(context, "outcome is 'accepted' or 'rejected'");
                return;
            }

            // Der Text ist Pflicht — auch bei einer Absage. Geprueft wird das im
            // Aggregat, damit es kein Handler und kein zweiter Endpunkt umgehen
            // kann; hier faellt es als 422 heraus (ADR-0042 §2).
            var vorgang = await mediator.Send(
                new BewertenBefehl(id, firma, ausgang, koerper?.Text), cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(vorgang, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------
    // Die Seite der Person
    // -----------------------------------------------------------------------

    private static void MapPersonenseite(IEndpointRouteBuilder proben)
    {
        // `/me` vor `/{id:guid}`: die Zwangsbedingung trennt die beiden
        // ohnehin, aber die Reihenfolge sagt, was gemeint ist.
        proben.MapGet("/me", async (
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

            // KEIN Ledger davor. Wer widerrufen hat, kaeme sonst an seine
            // eigenen Bewertungen nicht mehr heran — und eine Beurteilung, die
            // der Beurteilte nicht liest, ist genau das, was hier nicht gebaut
            // wird (ADR-0042 §2).
            var meine = await mediator.Send(
                new MeineVorgaengeAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new VorgangslisteV1([.. meine.Select(vorgang => Sicht(vorgang, context))]),
                cancellationToken);
        });

        proben.MapPost("/{id:guid}/submission", async (
            Guid id,
            EinreichenV1? koerper,
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

            var vorgang = await mediator.Send(
                new EinreichenBefehl(id, handelnder.Subject, koerper?.Text, koerper?.Url),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(vorgang, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------
    // Was beiden gehört
    // -----------------------------------------------------------------------

    private static void MapGemeinsam(IEndpointRouteBuilder proben)
    {
        /*
         * EINE Route für beide Seiten, und das ist kein Sparen an Zeilen.
         *
         * „Die Person sieht die Bewertung" (ADR-0042 §2) ist hier keine Regel,
         * sondern ein Bau: es gibt keine Firmensicht neben einer Personensicht,
         * weil es nur einen Endpunkt gibt, der einen Vorgang herausgibt — und
         * er baut sein Dokument mit derselben Methode. Zwei Sichten wären die
         * Stelle, an der die beiden auseinanderlaufen, und zwar erst Monate
         * später, beim nächsten Feld.
         *
         * Ein Test vergleicht die zwei Antworten Byte für Byte.
         */
        proben.MapGet("/{id:guid}", async (
            Guid id,
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

            var vorgang = await mediator.Send(
                new VorgangAbfrage(id, handelnder.Subject, Firma(akteur)), cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(vorgang, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------

    /// <summary>Das Unternehmen aus dem Token — nie aus der Anfrage.</summary>
    /// <remarks>
    /// Der Mandant kommt aus dem geprüften Token (ADR-0017/0018). Ihn aus einem
    /// Kopf oder einem Feld zu nehmen hiesse, den Aufrufer entscheiden zu
    /// lassen, in wessen Namen er spricht.
    /// </remarks>
    private static TenantId? Firma(ICurrentPrincipal akteur) =>
        akteur.Current?.Acting is Capacity.ForCompany firma ? firma.Tenant : null;

    /// <summary>Der eine Weg von einem Vorgang zu dem, was auf der Leitung steht.</summary>
    /// <remarks>
    /// Die Uhr kommt aus dem Container und nicht aus <c>DateTimeOffset.UtcNow</c>,
    /// damit ein Test eine Frist verstreichen lassen kann, ohne zu warten.
    /// </remarks>
    private static VorgangV1 Sicht(Vorgang vorgang, HttpContext context)
    {
        var uhr = context.RequestServices.GetRequiredService<TimeProvider>();

        return new VorgangV1(
            vorgang.Id,
            vorgang.Wer.Value,
            vorgang.Firma.Value,
            vorgang.Aufgabe.Titel,
            vorgang.Aufgabe.Text,
            vorgang.Aufgabe.Stunden,
            vorgang.Aufgabe.Frist,
            Woerter.Wort(vorgang.Stand_am(uhr.GetUtcNow())),
            vorgang.GestelltAm,
            vorgang.GeaendertAm)
        {
            Submission = vorgang.Einreichung is { } einreichung
                ? new EinreichungV1(einreichung.Text, einreichung.Adresse, einreichung.Am)
                : null,
            Evaluation = vorgang.Bewertung is { } bewertung
                ? new BewertungV1(
                    Woerter.Wort(bewertung.Ausgang), bewertung.Text, bewertung.Am)
                : null
        };
    }

    private static Task OhneFirma(ICurrentPrincipal akteur, HttpContext context) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            // 403 ist eine Aussage ueber den Aufrufer und verraet nichts ueber
            // den Menschen, nach dem er fragt.
            : ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Unbrauchbar(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed", detail);

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

    private static async ValueTask<object?> Abfangen(
        EndpointFilterInvocationContext aufruf, EndpointFilterDelegate weiter)
    {
        try
        {
            return await weiter(aufruf);
        }
        catch (KeinVorgang fehler)
        {
            // VIER Lagen, EINE Antwort: gibt es nicht; gehoert jemand anderem;
            // gehoert einem anderen Unternehmen; nichts (mehr) freigegeben. Ein
            // eigener Code fuer die letzte waere der „gesperrt"-Hinweis, der
            // verraet, dass es etwas gibt (ADR-0020 §1).
            await Schreibe(aufruf, StatusCodes.Status404NotFound, fehler.Message);

            return Geschrieben;
        }
        catch (Eingabefehler fehler)
        {
            await Schreibe(aufruf, StatusCodes.Status422UnprocessableEntity, fehler.Message);

            return Geschrieben;
        }
        catch (SchrittNichtMoeglich fehler)
        {
            // 409 und nicht 422: die Eingabe war in Ordnung, der Vorgang steht
            // nur woanders.
            await Schreibe(aufruf, StatusCodes.Status409Conflict, fehler.Message);

            return Geschrieben;
        }
        catch (EinwilligungSchweigt fehler)
        {
            // Ausdruecklich kein 404 und keine leere Liste: beides waere eine
            // Aussage ueber einen Menschen, die aus unserem Ausfall stammt
            // (ADR-0020 §3).
            await Schreibe(aufruf, StatusCodes.Status503ServiceUnavailable, fehler.Message);

            return Geschrieben;
        }
    }

    private static Task Schreibe(
        EndpointFilterInvocationContext aufruf, int status, string detail) =>
        ProblemDetailsMiddleware.Schreibe(
            aufruf.HttpContext, status, "Request failed", detail);
}
