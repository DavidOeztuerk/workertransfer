using Girder.Core.Identity;
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
public static class LebenslaufEndpoints
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
        lebenslaeufe.AddEndpointFilter(async (aufruf, weiter) =>
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
        });

        lebenslaeufe.MapPut("/me", async (
            LebenslaufSpeichernV1 body,
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
            // hiesse eine Anfrage, in der jemand ein Feld vergessen hat,
            // „loesche meinen ganzen Lebenslauf". Wer leeren will, schickt
            // ausdruecklich `[]`.
            if (body.Positions is null || body.Education is null)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "positions and education are required");
                return;
            }

            try
            {
                // Whose résumé this is comes from the verified token, never
                // from the body: a subject id on the wire would be a way to
                // write into somebody else's.
                var lebenslauf = await mediator.Send(
                    new LebenslaufSichernBefehl(
                        handelnder.Subject, Stationen(body), Ausbildungen(body)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(lebenslauf), cancellationToken);
            }
            catch (Lebenslaufregel regel)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", regel.Message);
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

            var lebenslauf = await mediator.Send(
                new MeinLebenslaufAbfrage(handelnder.Subject), cancellationToken);

            if (lebenslauf is null)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "no resume yet");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(lebenslauf), cancellationToken);
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
                new MeineAnfragenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(ansicht => Antwort(ansicht.Anfrage, ansicht.Aktiv)),
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

            var ergebnis = await mediator.Send(
                new ZugriffWiderrufenBefehl(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                // A statement about the caller, which leaks nothing: only a
                // company can ask for a résumé, and a private person knows they
                // are not one.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "only a company may ask for a resume");
                return;
            }

            var ergebnis = await mediator.Send(
                new LebenslaufAnfragenBefehl(
                    new SubjectId(subjectId), firma.Tenant, handelnder.Subject),
                cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken, StatusCodes.Status201Created);
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var anfragen = await mediator.Send(
                new FirmenanfragenAbfrage(firma.Tenant), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                anfragen.Select(anfrage => Antwort(anfrage, aktiv: null)), cancellationToken);
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var ergebnis = await mediator.Send(
                new SichtbarerLebenslaufAbfrage(new SubjectId(subjectId), firma.Tenant),
                cancellationToken);

            switch (ergebnis)
            {
                case Lebenslaufergebnis.Gefunden gefunden:
                    await context.Response.WriteAsJsonAsync(
                        Antwort(gefunden.Lebenslauf), cancellationToken);
                    return;

                default:
                    // Withheld and non-existent answer alike, and they must stay
                    // alike: a different answer would say whether this person
                    // has written a résumé, which is a fact about them.
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status404NotFound,
                        "Request failed", "no resume");
                    return;
            }
        });

        // ---------------------------------------------------------------
        // DIE UNTERLAGEN (ADR-0035)
        //
        // Sie haengen am Lebenslauf und nicht an der Bewerbung: eine Person
        // laedt ihr Zeugnis einmal hoch und legt es dann zwanzig Bewerbungen
        // bei. Je Bewerbung zu speichern hiesse, dieselbe Datei zwanzigmal zu
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var alle = await mediator.Send(
                new SichtbareUnterlagenAbfrage(new SubjectId(subjectId), firma.Tenant),
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

            if (handelnder.Acting is not Capacity.ForCompany firma)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            var gefunden = await mediator.Send(
                new SichtbarerUnterlagenInhaltAbfrage(
                    new SubjectId(subjectId), firma.Tenant, id),
                cancellationToken);

            if (gefunden is not { } treffer)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            context.Response.ContentType = treffer.Unterlage.Inhaltstyp;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + treffer.Unterlage.Id.ToString("N")
                + Typerkennung.Endung(treffer.Unterlage.Inhaltstyp) + "\"";

            await context.Response.Body.WriteAsync(treffer.Inhalt, cancellationToken);
        });

        var unterlagen = app.MapGroup("/resumes/me/documents");

        unterlagen.MapPost("/", async (
            HttpRequest anfrage,
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

            if (!anfrage.HasFormContentType)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status400BadRequest,
                    "Request failed", "expected multipart/form-data");
                return;
            }

            var formular = await anfrage.ReadFormAsync(cancellationToken);
            var datei = formular.Files.GetFile("file");

            if (datei is null || datei.Length == 0)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: file");
                return;
            }

            // Vor dem Lesen pruefen, nicht danach: eine Datei erst vollstaendig
            // in den Speicher zu ziehen und dann abzulehnen ist genau der Weg,
            // auf dem ein Aufrufer mit einer Adresse den Arbeitsspeicher fuellt.
            if (datei.Length > Unterlage.HoechsteGroesse)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "Request failed",
                    $"document must not exceed {Unterlage.HoechsteGroesse} bytes");
                return;
            }

            using var strom = new MemoryStream((int)datei.Length);
            await datei.CopyToAsync(strom, cancellationToken);

            var ergebnis = await mediator.Send(
                new UnterlageHinzufuegenBefehl(
                    handelnder.Subject,
                    formular["name"].ToString() is { Length: > 0 } genannt
                        ? genannt
                        : datei.FileName,
                    Artwahl(formular["kind"].ToString()),
                    strom.ToArray()),
                cancellationToken);

            switch (ergebnis)
            {
                case Unterlagenergebnis.Erledigt erledigt:
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.WriteAsJsonAsync(
                        Antwort(erledigt.Unterlage), cancellationToken);
                    return;

                case Unterlagenergebnis.ZuViele:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed",
                        $"at most {Unterlage.HoechsteAnzahl} documents");
                    return;

                case Unterlagenergebnis.Abgelehnt abgelehnt:
                    await ProblemDetailsMiddleware.Schreibe(
                        context, StatusCodes.Status422UnprocessableEntity,
                        "Request failed", abgelehnt.Grund);
                    return;
            }
        });

        unterlagen.MapGet("/", async (
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
                new MeineUnterlagenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                alle.Select(Antwort).ToArray(), cancellationToken);
        });

        unterlagen.MapGet("/{id:guid}/content", async (
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

            var gefunden = await mediator.Send(
                new UnterlageInhaltAbfrage(handelnder.Subject, id), cancellationToken);

            if (gefunden is not { } treffer)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such document");
                return;
            }

            // `inline` und nicht `attachment`: die Mappe zeigt die Unterlage im
            // Browser, statt sie herunterzuladen. Der Dateiname traegt die
            // Endung aus dem ERKANNTEN Typ, nicht aus dem hochgeladenen Namen.
            context.Response.ContentType = treffer.Unterlage.Inhaltstyp;
            context.Response.Headers.ContentDisposition =
                "inline; filename=\"" + treffer.Unterlage.Id.ToString("N")
                + Typerkennung.Endung(treffer.Unterlage.Inhaltstyp) + "\"";

            await context.Response.Body.WriteAsync(treffer.Inhalt, cancellationToken);
        });

        unterlagen.MapDelete("/{id:guid}", async (
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
                new UnterlageLoeschenBefehl(handelnder.Subject, id), cancellationToken);

            context.Response.StatusCode = weg
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

            // Eine unbekannte Vorlage wird ABGEWIESEN und nicht still auf die
            // Vorgabe gedreht: sonst waehlt jemand etwas, bekommt 200 zurueck
            // und sieht danach etwas anderes.
            if (Vorlagenwahl(koerper?.Template) is not { } vorlage)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: template");
                return;
            }

            var gesetzt = await mediator.Send(
                new VorlageWaehlenBefehl(handelnder.Subject, vorlage), cancellationToken);

            if (!gesetzt)
            {
                await ProblemDetailsMiddleware.Schreibe(
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
    /// Anfrage ohne Rumpf sieht der Aufrufer trotzdem sein 503 und nur das
    /// Protokoll trägt eine unbehandelte Ausnahme; bei einer mit Rumpf reißt
    /// die Verbindung, und er bekommt „Error while copying content to a
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

        var ergebnis = await mediator.Send(
            new AnfrageBeantwortenBefehl(id, handelnder.Subject, erteilen), cancellationToken);

        await Beantworte(context, ergebnis, cancellationToken);
    }

    /// <summary>
    /// Turns the domain's answers into status codes — in one place, so eight
    /// routes cannot drift apart.
    /// </summary>
    private static async Task Beantworte(
        HttpContext context,
        Anfrageergebnis ergebnis,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        switch (ergebnis)
        {
            case Anfrageergebnis.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Anfrage, aktiv: null), cancellationToken);
                return;

            case Anfrageergebnis.NichtSichtbar:
                // The same 404 a non-existent person gets. Asking requires the
                // profile release; without it the company must not be able to
                // tell "hidden" from "not here".
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "no such profile");
                return;

            case Anfrageergebnis.SchonGefragt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", "already asked");
                return;

            case Anfrageergebnis.Regelverstoss verstoss:
                await ProblemDetailsMiddleware.Schreibe(
                    context,
                    verstoss.Code == Anfrageregel.NichtDieBetroffene
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status409Conflict,
                    "Request failed",
                    verstoss.Erklaerung);
                return;
        }
    }

    private static LebenslaufV1 Antwort(Lebenslauf lebenslauf) => new(
        lebenslauf.Wer.Value,
        [.. lebenslauf.Stationen.Select(station => new StationV1(
            station.Arbeitgeber, station.Titel, station.Beginn.ToString(),
            station.Ende?.ToString(), station.Beschreibung, station.Technologien))],
        [.. lebenslauf.Ausbildungen.Select(ausbildung => new AusbildungV1(
            ausbildung.Einrichtung, ausbildung.Abschluss, ausbildung.Beginn.ToString(),
            ausbildung.Ende?.ToString()))],
        lebenslauf.Geaendert,
        Vorlagenwort(lebenslauf.Vorlage));

    /// <summary>Der Vorlagenname auf der Leitung.</summary>
    /// <remarks>
    /// Kleingeschrieben und deutsch, wie im Aggregat — ein zweites Wörterbuch
    /// wäre eine zweite Gelegenheit, sich zu verschreiben.
    /// </remarks>
    private static string Vorlagenwort(Vorlage vorlage) => vorlage switch
    {
        Vorlage.Klassisch => "klassisch",
        Vorlage.Modern => "modern",
        _ => "schlicht"
    };

    /// <summary>Die Vorlage zum Wort, oder <c>null</c>.</summary>
    private static Vorlage? Vorlagenwahl(string? wort) => wort switch
    {
        "schlicht" => Vorlage.Schlicht,
        "klassisch" => Vorlage.Klassisch,
        "modern" => Vorlage.Modern,
        _ => null
    };

    /// <summary>Das Wort zur Art einer Unterlage.</summary>
    private static string Artwort(Unterlagenart art) => art switch
    {
        Unterlagenart.Zeugnis => "zeugnis",
        Unterlagenart.Zertifikat => "zertifikat",
        _ => "sonstiges"
    };

    /// <summary>Die Art zum Wort — Unbekanntes wird „sonstiges".</summary>
    /// <remarks>
    /// Hier wird bewusst NICHT abgewiesen: die Art ordnet für den Menschen,
    /// der die Mappe liest, und sie wird nirgends ausgewertet. Eine Bewerbung
    /// an einem Tippfehler in einem Ordnungsbegriff scheitern zu lassen wäre
    /// Strenge am falschen Ort.
    /// </remarks>
    private static Unterlagenart Artwahl(string? wort) => wort switch
    {
        "zeugnis" => Unterlagenart.Zeugnis,
        "zertifikat" => Unterlagenart.Zertifikat,
        _ => Unterlagenart.Sonstiges
    };

    private static UnterlageV1 Antwort(Unterlage unterlage) => new(
        unterlage.Id,
        unterlage.Name,
        Artwort(unterlage.Art),
        unterlage.Inhaltstyp,
        unterlage.Groesse,
        unterlage.Hochgeladen);

    private static AnfrageV1 Antwort(Anfrage anfrage, bool? aktiv) => new(
        anfrage.Id,
        anfrage.Wer.Value,
        anfrage.Firma.Value,
        anfrage.Stand.ToString().ToUpperInvariant(),
        anfrage.Gestellt,
        anfrage.Beantwortet,
        aktiv);

    private static IReadOnlyList<Station> Stationen(LebenslaufSpeichernV1 body) =>
        [.. body.Positions.Select(station => Station.Aus(
            station.Employer, station.Title, Monat.Lies(station.StartedOn),
            station.EndedOn is null ? null : Monat.Lies(station.EndedOn),
            station.Description, station.Technologies))];

    private static IReadOnlyList<Ausbildung> Ausbildungen(LebenslaufSpeichernV1 body) =>
        [.. body.Education.Select(ausbildung => Ausbildung.Aus(
            ausbildung.Institution, ausbildung.Qualification, Monat.Lies(ausbildung.StartedOn),
            ausbildung.EndedOn is null ? null : Monat.Lies(ausbildung.EndedOn)))];

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
