using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Ablage;
using WorkerTransfer.Resume.Application.Anfragen;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.Resume.Application.Lebenslaeufe;
using WorkerTransfer.Resume.Contracts;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Api;

/// <summary><c>/resumes/*</c> — writing one, asking for one, answering.</summary>
/// <remarks>
/// There is no public switch anywhere here, and none may be added. A profile is
/// a notice board; a résumé names real employers with dates — exactly what a
/// current employer must not see. A company asks, the person answers, and the
/// release covers that one company.
/// </remarks>
public static class ResumeEndpoints
{
    /// <summary>Maps the seven endpoints of the résumé.</summary>
    public static IEndpointRouteBuilder MapLebenslaufEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var lebenslaeufe = app.MapGroup("/resumes");

        // A silent ledger is neither a yes nor a no. Answering 404 would say
        // the person is not there; answering with the résumé would hand out
        // what nobody confirmed may be handed out. 503 says the one true
        // thing: this service cannot answer right now.
        //
        // Here as one filter rather than a catch in each route, because the
        // route that forgets it is the one that answers wrongly — and it looks
        // like a working route until the ledger is down.
        lebenslaeufe.AddEndpointFilter(async (call, weiter) =>
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
        });

        lebenslaeufe.MapPut("/me", async (
            StoreResumeV1 body,
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

            // Ein fehlendes Feld ist NICHT dasselbe wie eine leere Liste.
            //
            // `{}` liess frueher `null.Select(...)` laufen und kam als 500
            // zurueck (D2). Es einfach als leer zu lesen waere schlimmer: dann
            // hiesse eine ResumeRequest, in der jemand ein Feld vergessen hat,
            // „loesche meinen ganzen CurriculumVitae". Wer leeren will, schickt
            // ausdruecklich `[]`.
            if (body.Positions is null || body.Education is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "positions and education are required");
                return;
            }

            try
            {
                // Whose résumé this is comes from the verified token, never
                // from the body: a subject id on the wire would be a way to
                // write into somebody else's.
                var resume = await mediator.Send(
                    new SaveResumeCommand(
                        handelnder.Subject, Stationen(body), Ausbildungen(body)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(resume), cancellationToken);
            }
            catch (ResumeRule rule)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", rule.Message);
            }
        });

        lebenslaeufe.MapGet("/me", async (
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

            var resume = await mediator.Send(
                new MyResumeQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                resume is null ? null : Antwort(resume), cancellationToken);
        });

        // The person's own list, and the one place where "was granted" and
        // "holds now" visibly come apart: a withdrawn release still shows
        // GRANTED, with `active: false` beside it.
        lebenslaeufe.MapGet("/me/requests", async (
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

            var meine = await mediator.Send(
                new MyRequestsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(ansicht => Antwort(ansicht.ResumeRequest, ansicht.Aktiv)),
                cancellationToken);
        });

        lebenslaeufe.MapPost("/requests/{id:guid}/grant", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: true, mediator, akteur, context, cancellationToken));

        lebenslaeufe.MapPost("/requests/{id:guid}/decline", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Beantworte(id, erteilen: false, mediator, akteur, context, cancellationToken));

        // Taking a release back. No notification goes out for this: pushing it
        // at the company would turn withdrawing into a confrontation, and the
        // whole point of reading the ledger fresh is that it costs the person
        // nothing. The company notices the same way it would anyway — the next
        // read comes up empty.
        lebenslaeufe.MapPost("/requests/{id:guid}/revoke", async (
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

            var result = await mediator.Send(
                new RevokeAccessCommand(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, result, cancellationToken);
        });

        // A company asks. It needs the PROFILE release to do so — never the
        // existence of a résumé: "has already written one" is a fact about the
        // person that nobody should be able to probe for.
        lebenslaeufe.MapPost("/{subjectId:guid}/requests", async (
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
                // A statement about the caller, which leaks nothing: only a
                // company can ask for a résumé, and a private person knows they
                // are not one.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "only a company may ask for a resume");
                return;
            }

            var result = await mediator.Send(
                new RequestResumeCommand(
                    new SubjectId(subjectId), company.Tenant, handelnder.Subject),
                cancellationToken);

            await Beantworte(context, result, cancellationToken, StatusCodes.Status201Created);
        });

        // The company's own list. `active` is null here: the company already has
        // the answer in the form of the data it does or does not get, and a
        // field here could be polled without ever reading a résumé.
        lebenslaeufe.MapGet("/requests", async (
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var requests = await mediator.Send(
                new CompanyRequestsQuery(company.Tenant), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                requests.Select(request => Antwort(request, aktiv: null)), cancellationToken);
        });

        lebenslaeufe.MapGet("/{subjectId:guid}", async (
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var result = await mediator.Send(
                new VisibleResumeQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            switch (result)
            {
                case ResumeResult.Gefunden found:
                    await context.Response.WriteAsJsonAsync(
                        Antwort(found.CurriculumVitae), cancellationToken);
                    return;

                default:
                    // Withheld and non-existent answer alike, and they must stay
                    // alike: a different answer would say whether this person
                    // has written a résumé, which is a fact about them.
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status404NotFound,
                        "Request failed", "no resume");
                    return;
            }
        });

        // ---------------------------------------------------------------
        // DIE UNTERLAGEN (ADR-0035)
        //
        // Sie haengen am CurriculumVitae und nicht an der JobApplication: eine Person
        // laedt ihr Reference einmal hoch und legt es dann zwanzig Applications
        // bei. Je JobApplication zu speichern hiesse, dieselbe Datei zwanzigmal zu
        // halten — und beim Loeschen neunzehnmal daneben zu greifen.
        // ---------------------------------------------------------------

        // DER BLICK DES UNTERNEHMENS auf die Mappe. Er haengt am Ledger und
        // an nichts sonst — keine Bewerbungstabelle wird befragt (ADR-0020).
        lebenslaeufe.MapGet("/{subjectId:guid}/documents", async (
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
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var alle = await mediator.Send(
                new VisibleDocumentsQuery(new SubjectId(subjectId), company.Tenant),
                cancellationToken);

            // Eine leere Liste, wenn nichts freigegeben ist — und dieselbe
            // leere Liste, wenn es nichts gibt. Ein Unterschied verriete, dass
            // diese Person Unterlagen HAT, und das ist eine Tatsache ueber sie.
            await context.Response.WriteAsJsonAsync(
                alle.Select(Antwort).ToArray(), cancellationToken);
        });

        lebenslaeufe.MapGet("/{subjectId:guid}/documents/{id:guid}/content", async (
            Guid subjectId,
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

            if (handelnder.Acting is not Capacity.ForCompany company)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var found = await mediator.Send(
                new VisibleDocumentContentQuery(
                    new SubjectId(subjectId), company.Tenant, id),
                cancellationToken);

            if (found is not { } hit)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            context.Response.ContentType = hit.DocumentRecord.Inhaltstyp;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + hit.DocumentRecord.Id.ToString("N")
                + Typerkennung.Endung(hit.DocumentRecord.Inhaltstyp) + "\"";

            await context.Response.Body.WriteAsync(hit.Inhalt, cancellationToken);
        });

        var documents = app.MapGroup("/resumes/me/documents");

        documents.MapPost("/", async (
            HttpRequest request,
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

            if (!request.HasFormContentType)
            {
                // 415 UND NICHT 400: die ResumeRequest ist wohlgeformt, nur ihr
                // Medienformat wird hier nicht bedient. 400 hiesse „ich konnte
                // deinen Rumpf nicht lesen" und schickt den Aufrufer auf die
                // Search nach einem Tippfehler, den es nicht gibt.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status415UnsupportedMediaType,
                    "Request failed", "expected multipart/form-data");
                return;
            }

            var formular = await request.ReadFormAsync(cancellationToken);
            var file = formular.Files.GetFile("file");

            if (file is null || file.Length == 0)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: file");
                return;
            }

            // Vor dem Lesen pruefen, nicht danach: eine Datei erst vollstaendig
            // in den Speicher zu ziehen und dann abzulehnen ist genau der Weg,
            // auf dem ein Aufrufer mit einer Adresse den Arbeitsspeicher fuellt.
            if (file.Length > DocumentRecord.HoechsteGroesse)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "Request failed",
                    $"document must not exceed {DocumentRecord.HoechsteGroesse} bytes");
                return;
            }

            using var strom = new MemoryStream((int)file.Length);
            await file.CopyToAsync(strom, cancellationToken);

            var result = await mediator.Send(
                new AddDocumentCommand(
                    handelnder.Subject,
                    formular["name"].ToString() is { Length: > 0 } genannt
                        ? genannt
                        : file.FileName,
                    Artwahl(formular["kind"].ToString()),
                    strom.ToArray()),
                cancellationToken);

            switch (result)
            {
                case DocumentResult.Erledigt erledigt:
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        Antwort(erledigt.DocumentRecord), cancellationToken);
                    return;

                case DocumentResult.ZuViele:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed",
                        $"at most {DocumentRecord.HoechsteAnzahl} documents");
                    return;

                case DocumentResult.Abgelehnt abgelehnt:
                    await ProblemDetailsMiddleware.Write(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", abgelehnt.Grund);
                    return;
            }
        });

        documents.MapGet("/", async (
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

            var alle = await mediator.Send(
                new MyDocumentRecordsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                alle.Select(Antwort).ToArray(), cancellationToken);
        });

        /*
         * DER KNOPF — und er ist der ganze Unterschied zu einem Hintergrundlauf.
         *
         * ADR-0004 verbietet Scraping; ein Nachtlauf ueber die Ablage hielte
         * seinen Buchstaben und verfehlte seinen Sinn — eine Plattform, die
         * einem Menschen dauerhaft hinterhersieht, tut etwas anderes als eine,
         * die einmal auf seine Bitte hinsieht. Deshalb gibt es genau hier eine
         * Adresse, sie wird von einem Klick gerufen, und im Hochladepfad steht
         * kein Aufruf des Erkenners. Gemessen in `ErkennungsreiseTests`, nicht
         * behauptet: nach einem Upload steht der Zaehler des Erkenners auf null.
         *
         * POST und nicht GET, weil es SCHREIBT: der Fund wird abgelegt. Eine
         * Abfrage liefe an `TransaktionsBehavior` vorbei und die Zeile wuerde
         * nie festgeschrieben — dieselbe Falle wie `POST /me/oauth/start`.
         */
        documents.MapPost("/read", async (
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

            var state = await mediator.Send(
                new ReadDocumentsCommand(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                state.Select(Antwort).ToArray(), cancellationToken);
        });

        /*
         * Was zuletzt gelesen wurde — OHNE zu lesen.
         *
         * Die ProfilePage fragt das beim Load, und sie darf das, weil hier
         * nichts geschieht: keine Datei wird geoeffnet, kein Erkenner gerufen,
         * keine Zeile geschrieben. Wer diese Trennung aufhebt und den
         * Lesevorgang hier anhaengt, macht aus dem Oeffnen einer Seite genau
         * den Hintergrundlauf, den der Knopf daneben vermeidet.
         */
        documents.MapGet("/terms", async (
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

            var state = await mediator.Send(
                new MyTermsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                state.Select(Antwort).ToArray(), cancellationToken);
        });

        documents.MapGet("/{id:guid}/content", async (
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

            var found = await mediator.Send(
                new DocumentContentQuery(handelnder.Subject, id), cancellationToken);

            if (found is not { } hit)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            // `inline` und nicht `attachment`: die Mappe zeigt die DocumentRecord im
            // Browser, statt sie herunterzuladen. Der Dateiname traegt die
            // Endung aus dem ERKANNTEN Typ, nicht aus dem hochgeladenen Namen.
            context.Response.ContentType = hit.DocumentRecord.Inhaltstyp;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + hit.DocumentRecord.Id.ToString("N")
                + Typerkennung.Endung(hit.DocumentRecord.Inhaltstyp) + "\"";

            await context.Response.Body.WriteAsync(hit.Inhalt, cancellationToken);
        });

        documents.MapDelete("/{id:guid}", async (
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

            var weg = await mediator.Send(
                new DeleteDocumentRecordCommand(handelnder.Subject, id), cancellationToken);

            context.Response.StatusCode = weg
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        documents.MapPut("/{id:guid}/as-cv", async (
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

            var gesetzt = await mediator.Send(
                new MarkDocumentAsResumeCommand(handelnder.Subject, id), cancellationToken);

            context.Response.StatusCode = gesetzt
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        lebenslaeufe.MapPut("/me/template", async (
            VorlageWaehlenV1 koerper,
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

            // Eine unbekannte ResumeTemplate wird ABGEWIESEN und nicht still auf die
            // Vorgabe gedreht: sonst waehlt jemand etwas, bekommt 200 zurueck
            // und sieht danach etwas anderes.
            if (Vorlagenwahl(koerper?.Template) is not { } template)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: template");
                return;
            }

            var gesetzt = await mediator.Send(
                new VorlageWaehlenBefehl(handelnder.Subject, template), cancellationToken);

            if (!gesetzt)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no resume");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
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

    private static async Task Beantworte(
        Guid id,
        bool erteilen,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (akteur.Current is not { } handelnder)
        {
            await NichtAngemeldet(context);
            return;
        }

        var result = await mediator.Send(
            new AnswerRequestCommand(id, handelnder.Subject, erteilen), cancellationToken);

        await Beantworte(context, result, cancellationToken);
    }

    /// <summary>
    /// Turns the domain's answers into status codes — in one place, so eight
    /// routes cannot drift apart.
    /// </summary>
    private static async Task Beantworte(
        HttpContext context,
        ResumeRequestResult result,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        switch (result)
        {
            case ResumeRequestResult.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.ResumeRequest, aktiv: null), cancellationToken);
                return;

            case ResumeRequestResult.NichtSichtbar:
                // The same 404 a non-existent person gets. Asking requires the
                // profile release; without it the company must not be able to
                // tell "hidden" from "not here".
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;

            case ResumeRequestResult.SchonGefragt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "already asked");
                return;

            case ResumeRequestResult.Regelverstoss verstoss:
                await ProblemDetailsMiddleware.Write(
                    context,
                    verstoss.Code == ResumeRequestRule.NichtDieBetroffene
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status409Conflict,
                    "Request failed",
                    verstoss.Erklaerung);
                return;
        }
    }

    private static ResumeV1 Antwort(CurriculumVitae resume) => new(
        resume.Wer.Value,
        [.. resume.Stationen.Select(station => new StationV1(
            station.Arbeitgeber, station.Titel, station.Beginn.ToString(),
            station.Ende?.ToString(), station.Beschreibung, station.Technologien))],
        [.. resume.Ausbildungen.Select(ausbildung => new AusbildungV1(
            ausbildung.Einrichtung, ausbildung.Abschluss, ausbildung.Beginn.ToString(),
            ausbildung.Ende?.ToString(),
            ausbildung.Art == Ausbildungsart.Schule ? "schule" : "ausbildung"))],
        resume.Geaendert,
        Vorlagenwort(resume.ResumeTemplate));

    /// <summary>Der Vorlagenname auf der Leitung.</summary>
    /// <remarks>
    /// Kleingeschrieben und deutsch, wie im Aggregat — ein zweites Wörterbuch
    /// wäre eine zweite Gelegenheit, sich zu verschreiben.
    /// </remarks>
    private static string Vorlagenwort(ResumeTemplate template) => template switch
    {
        ResumeTemplate.Classic => "klassisch",
        ResumeTemplate.Modern => "modern",
        _ => "schlicht"
    };

    /// <summary>Die ResumeTemplate zum Wort, oder <c>null</c>.</summary>
    private static ResumeTemplate? Vorlagenwahl(string? word) => word switch
    {
        "schlicht" => ResumeTemplate.Plain,
        "klassisch" => ResumeTemplate.Classic,
        "modern" => ResumeTemplate.Modern,
        _ => null
    };

    /// <summary>Das Wort zur Art einer DocumentRecord.</summary>
    private static string Artwort(DocumentKind kind) => kind switch
    {
        DocumentKind.Reference => "zeugnis",
        DocumentKind.Certificate => "zertifikat",
        DocumentKind.CurriculumVitae => "lebenslauf",
        _ => "sonstiges"
    };

    /// <summary>Die Art zum Wort — Unbekanntes wird „sonstiges".</summary>
    /// <remarks>
    /// Hier wird bewusst NICHT abgewiesen: die Art ordnet für den Menschen,
    /// der die Mappe liest, und sie wird nirgends ausgewertet. Eine JobApplication
    /// an einem Tippfehler in einem Ordnungsbegriff scheitern zu lassen wäre
    /// Strenge am falschen Ort.
    /// </remarks>
    private static DocumentKind Artwahl(string? word) => word switch
    {
        "zeugnis" => DocumentKind.Reference,
        "zertifikat" => DocumentKind.Certificate,
        "lebenslauf" => DocumentKind.CurriculumVitae,
        _ => DocumentKind.Other
    };

    private static DocumentRecordV1 Antwort(DocumentRecord document) => new(
        document.Id,
        document.Name,
        Artwort(document.Art),
        document.Inhaltstyp,
        document.Groesse,
        document.Hochgeladen);

    /// <summary>Der State einer DocumentRecord — gelesen oder nicht, und was drin stand.</summary>
    /// <remarks>
    /// <c>read_at: null</c> heisst „noch nie gelesen" und ist etwas anderes als
    /// eine leere Wortliste. Der Name reist mit, damit die Oberflaeche sagen
    /// kann, aus WELCHER Datei nichts zu lesen war — „nichts gefunden" ohne die
    /// Datei dazu waere wieder die stillschweigende Behauptung, es stehe nichts
    /// darin (ADR-0022 §3).
    /// </remarks>
    private static DocumentTermsV1 Antwort(DocumentRecordState state) => new(
        state.DocumentRecord.Id,
        state.DocumentRecord.Name,
        state.Fund?.Gelesen,
        state.Fund?.TextGefunden ?? false,
        state.Fund?.Begriffe ?? []);

    private static RequestV1 Antwort(ResumeRequest request, bool? aktiv) => new(
        request.Id,
        request.Wer.Value,
        request.Firma.Value,
        request.State.ToString().ToUpperInvariant(),
        request.Gestellt,
        request.Beantwortet,
        aktiv);

    private static IReadOnlyList<Station> Stationen(StoreResumeV1 body) =>
        [.. body.Positions.Select(station => Station.Aus(
            station.Employer, station.Title, Monat.Lies(station.StartedOn),
            station.EndedOn is null ? null : Monat.Lies(station.EndedOn),
            station.Description, station.Technologies))];

    private static IReadOnlyList<Ausbildung> Ausbildungen(StoreResumeV1 body) =>
        [.. body.Education.Select(ausbildung => Ausbildung.Aus(
            ausbildung.Institution, ausbildung.Qualification, Monat.Lies(ausbildung.StartedOn),
            ausbildung.EndedOn is null ? null : Monat.Lies(ausbildung.EndedOn),
            ausbildung.Kind == "schule" ? Ausbildungsart.Schule : Ausbildungsart.Ausbildung))];

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
