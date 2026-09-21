using Noelia.Core.Identity;
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
/// None Route, kein Zustand, kein Feld. Wer eine Aufgabe nicht will, tut
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
public static class WorkSampleEndpoints
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
            SetTaskV1? koerper,
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

            if (koerper.DueAt is not { } deadline)
            {
                await Unbrauchbar(context, "due_at is required");
                return;
            }

            var caseItem = await mediator.Send(
                new SetTaskCommand(
                    new SubjectId(koerper.SubjectId),
                    company,
                    koerper.Title,
                    koerper.Task,
                    stunden,
                    deadline),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;

            await context.Response.WriteAsJsonAsync(Sicht(caseItem, context), cancellationToken);
        });

        proben.MapGet("/", async (
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

            var unsere = await mediator.Send(
                new CompanyCasesQuery(company), cancellationToken);

            // None Gesamtzahl (ADR-0026): sie verriete ueber die Differenz zur
            // Laenge, wie viele Menschen dem Unternehmen die Sicht entzogen
            // haben.
            await context.Response.WriteAsJsonAsync(
                new CaseListV1([.. unsere.Select(caseItem => Sicht(caseItem, context))]),
                cancellationToken);
        });

        proben.MapPost("/{id:guid}/evaluation", async (
            Guid id,
            EvaluateV1? koerper,
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

            if (Woerter.LiesAusgang(koerper?.Outcome) is not { } ausgang)
            {
                await Unbrauchbar(context, "outcome is 'accepted' or 'rejected'");
                return;
            }

            // Der Text ist Pflicht — auch bei einer Absage. Geprueft wird das im
            // Aggregat, damit es kein Handler und kein zweiter Endpunkt umgehen
            // kann; hier faellt es als 422 heraus (ADR-0042 §2).
            var caseItem = await mediator.Send(
                new EvaluateCommand(id, company, ausgang, koerper?.Text), cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(caseItem, context), cancellationToken);
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
                new MyCasesQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new CaseListV1([.. meine.Select(caseItem => Sicht(caseItem, context))]),
                cancellationToken);
        });

        proben.MapPost("/{id:guid}/submission", async (
            Guid id,
            SubmitV1? koerper,
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

            var caseItem = await mediator.Send(
                new SubmitCommand(id, handelnder.Subject, koerper?.Text, koerper?.Url),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(caseItem, context), cancellationToken);
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
         * „Die Person sieht die Evaluation" (ADR-0042 §2) ist hier keine Regel,
         * sondern ein Bau: es gibt keine Firmensicht neben einer Personensicht,
         * weil es nur einen Endpunkt gibt, der einen Case herausgibt — und
         * er baut sein Dokument mit derselben Methode. Zwei Sichten wären die
         * Job, an der die beiden auseinanderlaufen, und zwar erst Monate
         * später, beim nächsten Feld.
         *
         * Ein Test vergleicht die zwei Responses Byte für Byte.
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

            var caseItem = await mediator.Send(
                new CaseQuery(id, handelnder.Subject, Firma(akteur)), cancellationToken);

            await context.Response.WriteAsJsonAsync(Sicht(caseItem, context), cancellationToken);
        });
    }

    // -----------------------------------------------------------------------

    /// <summary>Das Unternehmen aus dem Token — nie aus der ResumeRequest.</summary>
    /// <remarks>
    /// Der Mandant kommt aus dem geprüften Token (ADR-0017/0018). Ihn aus einem
    /// Header oder einem Feld zu nehmen hiesse, den Aufrufer entscheiden zu
    /// lassen, in wessen Namen er spricht.
    /// </remarks>
    private static TenantId? Firma(ICurrentPrincipal akteur) =>
        akteur.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    /// <summary>Der eine Weg von einem Case zu dem, was auf der Leitung steht.</summary>
    /// <remarks>
    /// Die Uhr kommt aus dem Container und nicht aus <c>DateTimeOffset.UtcNow</c>,
    /// damit ein Test eine Frist verstreichen lassen kann, ohne zu warten.
    /// </remarks>
    private static CaseV1 Sicht(Case caseItem, HttpContext context)
    {
        var uhr = context.RequestServices.GetRequiredService<TimeProvider>();

        return new CaseV1(
            caseItem.Id,
            caseItem.Wer.Value,
            caseItem.Firma.Value,
            caseItem.Aufgabe.Titel,
            caseItem.Aufgabe.Text,
            caseItem.Aufgabe.Stunden,
            caseItem.Aufgabe.Frist,
            Woerter.Wort(caseItem.Stand_am(uhr.GetUtcNow())),
            caseItem.GestelltAm,
            caseItem.GeaendertAm)
        {
            Submission = caseItem.Submission is { } submission
                ? new SubmissionV1(submission.Text, submission.Adresse, submission.Am)
                : null,
            Evaluation = caseItem.Evaluation is { } evaluation
                ? new EvaluationV1(
                    Woerter.Wort(evaluation.Ausgang), evaluation.Text, evaluation.Am)
                : null
        };
    }

    private static Task OhneFirma(ICurrentPrincipal akteur, HttpContext context) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            // 403 ist eine Aussage ueber den Aufrufer und verraet nichts ueber
            // den Menschen, nach dem er fragt.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Unbrauchbar(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed", detail);

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

    private static async ValueTask<object?> Abfangen(
        EndpointFilterInvocationContext call, EndpointFilterDelegate weiter)
    {
        try
        {
            return await weiter(call);
        }
        catch (NoCase error)
        {
            // VIER Lagen, EINE Antwort: gibt es nicht; gehoert jemand anderem;
            // gehoert einem anderen Unternehmen; nichts (mehr) freigegeben. Ein
            // eigener Code fuer die letzte waere der „gesperrt"-Hinweis, der
            // verraet, dass es etwas gibt (ADR-0020 §1).
            await Write(call, StatusCodes.Status404NotFound, error.Message);

            return Geschrieben;
        }
        catch (Eingabefehler error)
        {
            await Write(call, StatusCodes.Status422UnprocessableEntity, error.Message);

            return Geschrieben;
        }
        catch (SchrittNichtMoeglich error)
        {
            // 409 und nicht 422: die Eingabe war in Ordnung, der Case steht
            // nur woanders.
            await Write(call, StatusCodes.Status409Conflict, error.Message);

            return Geschrieben;
        }
        catch (ConsentGateSilent error)
        {
            // Ausdruecklich kein 404 und keine leere Liste: beides waere eine
            // Aussage ueber einen Menschen, die aus unserem Ausfall stammt
            // (ADR-0020 §3).
            await Write(call, StatusCodes.Status503ServiceUnavailable, error.Message);

            return Geschrieben;
        }
    }

    private static Task Write(
        EndpointFilterInvocationContext call, int status, string detail) =>
        ProblemDetailsMiddleware.Write(
            call.HttpContext, status, "Request failed", detail);
}
