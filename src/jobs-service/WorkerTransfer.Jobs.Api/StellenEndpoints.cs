using System.Globalization;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Api.Berechtigung;
using WorkerTransfer.Jobs.Application.Entwurf;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Jobs;
using WorkerTransfer.Jobs.Contracts;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>/jobs</c> und <c>/companies/me/jobs</c>.</summary>
public static class JobEndpoints
{
    /// <summary>Bindet die sieben Stellenrouten ein.</summary>
    public static IEndpointRouteBuilder MapStellenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var jobs = app.MapGroup("/jobs");

        jobs.MapPost("/draft", async (
            DraftV1 koerper,
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
                await ProblemDetailsMiddleware.Write(
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
                    new DraftAdvertQuery(
                        koerper.Title, koerper.Description, koerper.Skills ?? [],
                        koerper.Location, koerper.Wish),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (DraftUnavailable error)
            {
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

        jobs.MapPost("/", async (
            WriteJobV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var company))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var job = await mediator.Send(
                    new CreateJobCommand(company, Angaben(koerper)), cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(Antwort(job), cancellationToken);
            });
        });

        jobs.MapPut("/{id:guid}", async (
            Guid id,
            WriteJobV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var company))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var job = await mediator.Send(
                    new ChangeJobCommand(company, id, Angaben(koerper)), cancellationToken);

                await Zeige(context, job, cancellationToken);
            });
        });

        // Die beiden Zustandswechsel sind die einzigen Jobs hier, an denen
        // die Anzeige das Unternehmen nach aussen vertritt — und deshalb die
        // einzigen, die einen `admin` verlangen. Schreiben und Aendern bleiben
        // jedem Mitglied: ein Entwurf steht niemandem gegenueber.
        //
        // Aufgeloest wird die Richtlinie von `AdminPermission` aus der
        // Mitgliedschaftstabelle von identity-service, nicht aus dem Token —
        // wer entfernt wird, ist bei der naechsten ResumeRequest draussen.
        jobs.MapPost("/{id:guid}/publish", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, JobState.Published, mediator, akteur, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Richtlinie(JobPermissions.Veroeffentlichen));

        jobs.MapPost("/{id:guid}/close", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, JobState.Closed, mediator, akteur, context, cancellationToken))
            .RequireAuthorization(
                AdminPermissionExtensions.Richtlinie(JobPermissions.Schliessen));

        // Öffentlich für jeden Angemeldeten — und ohne jede Sortierung nach
        // Passung. Wie gut jemand zu einer Job passt, rechnet der Browser
        // und zeigt es der Person (ADR-0022).
        // OHNE Anmeldung, und das ist eine Korrektur.
        //
        // Hier stand eine Sperre auf `akteur.Current is null`. Sie gab nichts
        // preis, was nicht ohnehin oeffentlich waere — der Speicher filtert auf
        // `status == "published"`, und veroeffentlicht heisst in dieser Domaene
        // "fuer alle sichtbar, auch ohne Konto". Sie kostete aber genau das,
        // wofuer eine Anzeige da ist: `/careers/<kuerzel>` ist als oeffentliche
        // Seite dokumentiert (docs/routenkarte.yml) und zeigte einem anonymen
        // Besucher trotzdem keine einzige Job. Eine Ausschreibung, die man
        // nur nach Anmeldung sieht, erreicht genau die nicht, fuer die sie
        // gedacht ist.
        //
        // Entwuerfe und geschlossene Anzeigen bleiben drinnen: der State wird
        // im Speicher gefiltert und nie vom Aufrufer.
        jobs.MapGet("/", async (
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var request = context.Request.Query;

            // `q`, `company` und `employment` wurden von der Oberflaeche seit
            // jeher geschickt und hier NIE gelesen. Die Suchmaske suchte also
            // nichts, der Beschaeftigungsfilter filterte nichts — und die
            // Karriereseite zeigte die Anzeigen ALLER Unternehmen unter dem
            // Namen eines einzigen.
            var ort = request["location"].ToString();

            // Der Umkreis kommt als drei Zahlen und wird HIER zu einem Wert
            // oder zu nichts. `lat` und `lon` sind bereits im Browser gerundet
            // (zwei Nachkommastellen, gut ein Kilometer) — sie stehen in einer
            // Adresszeile und damit in Zugriffsprotokollen, und genauer könnte
            // an keiner Antwort etwas ändern, weil die Orte der Anzeigen
            // Stadtmittelpunkte sind.
            var radius = Zahl(request["radius_km"]);

            var umkreis = Umkreis.Aus(Zahl(request["lat"]), Zahl(request["lon"]), radius)
                // OHNE Koordinaten ist der ORT die Mitte. Wer „Leipzig" tippt
                // und „50 km" wählt, meint „um Leipzig herum" — und braucht
                // dafür weder GPS noch die Erlaubnis dazu.
                ?? (PlaceLookup.Find(null, ort) is { } mitte
                    ? Umkreis.Aus(mitte.Breite, mitte.Laenge, radius)
                    : null);

            var page = await mediator.Send(
                new JobSearchQuery(
                    PageSelection.Seite(request["page"]),
                    PageSelection.Groesse(request["page_size"]),
                    request["skill"].Count > 0 ? [.. request["skill"]!] : null,
                    // MIT Umkreis ist der Ort die MITTE und kein Textfilter.
                    // Beides zugleich hiesse „innerhalb von 25 km UND das Wort
                    // muss vorkommen" — und schlösse genau die Nachbarorte aus,
                    // wegen derer jemand einen Umkreis wählt.
                    umkreis is null ? ort : string.Empty,
                    Grad(request["remote"]),
                    request["q"].ToString(),
                    Guid.TryParse(request["company"], out var company) ? company : null,
                    request["employment"].ToString(),
                    umkreis),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new PageResponse<JobV1>(
                    [.. page.Eintraege.Select(Antwort)],
                    PageSelection.Seite(request["page"]),
                    PageSelection.Groesse(request["page_size"]),
                    page.Gesamt,
                    // Nur bei einer Umkreissuche. Ohne sie wurde nichts
                    // ausgelassen, und eine `0` behauptete, die Frage sei
                    // gestellt worden.
                    umkreis is null ? null : page.OhneOrt),
                cancellationToken);
        });

        // Der Ort zu einem Punkt — die Gegenrichtung der Umkreissuche.
        //
        // Ohne Anmeldung, wie die Liste selbst: geantwortet wird aus einer
        // Tabelle, die im Bild mitreist (ADR-0032). Sie sagt nichts ueber
        // irgendjemanden, und der Punkt, der hereinkommt, ist derselbe bereits
        // gerundete, der auch an die Search geht.
        jobs.MapGet("/place", async (
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var punkt = Zahl(context.Request.Query["lat"]) is { } breite
                        && Zahl(context.Request.Query["lon"]) is { } length
                        && breite is >= -90 and <= 90
                        && length is >= -180 and <= 180
                ? new Ortspunkt(breite, length)
                : (Ortspunkt?)null;

            // Kein Ort in der Naehe ist kein Fehler, sondern eine Auskunft: der
            // Punkt liegt ausserhalb von DE, AT und CH. Ein 404 waere hier
            // irrefuehrend — die Adresse gibt es.
            await context.Response.WriteAsJsonAsync(
                new { location = punkt is { } p ? PlaceLookup.NaechsterOrt(p) : null },
                cancellationToken);
        });

        // Ohne Anmeldung lesbar, genau wie die Liste — und aus denselben zwei
        // Gruenden. Die Karriereseite ist eine Adresse, die man weitergibt: wer
        // sie oeffnet, hat kein Konto, und ein 401 machte aus der Anzeige eine
        // Anmeldeaufforderung. Und applications-service fragt hier
        // Dienst-zu-Dienst nach, ob es die Job gibt — ohne Token, weil er
        // keines hat; das Tor liess deshalb JEDE JobApplication mit 503 enden.
        //
        // Sicher ist das ohne Zutun: der Handler gibt eine Job nur heraus,
        // wenn sie dem fragenden Unternehmen gehoert ODER veroeffentlicht ist.
        // Ohne Token ist die Firma `null`, also bleibt allein das
        // Veroeffentlichte — Entwurf, geschlossen und nicht vorhanden sind
        // ununterscheidbar 404.
        jobs.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var job = await mediator.Send(
                new JobQuery(
                    id,
                    akteur.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null),
                cancellationToken);

            await Zeige(context, job, cancellationToken);
        });

        app.MapGet("/companies/me/jobs", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var company))
            {
                await KeineFirma(context, akteur);
                return;
            }

            var meine = await mediator.Send(new MyJobsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort), cancellationToken);
        });

        return app;
    }

    private static async Task Wechsle(
        Guid id,
        JobState target,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Firma(akteur, out var company))
        {
            await KeineFirma(context, akteur);
            return;
        }

        await Beantworte(context, cancellationToken, async () =>
        {
            var job = await mediator.Send(
                new ChangeStateCommand(company, id, target), cancellationToken);

            await Zeige(context, job, cancellationToken);
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
    private static Task Zeige(HttpContext context, Job? job, CancellationToken cancellationToken) =>
        job is null
            ? ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status404NotFound, "Request failed", "no such job")
            : context.Response.WriteAsJsonAsync(Antwort(job), cancellationToken);

    private static async Task Beantworte(
        HttpContext context, CancellationToken cancellationToken, Func<Task> arbeit)
    {
        try
        {
            await arbeit();
        }
        catch (UebergangNichtErlaubt error)
        {
            // 409, nicht 422: der Aufrufer darf das grundsätzlich, nur diese
            // Anzeige ist gerade nicht in dem Zustand.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status409Conflict, "Request failed", error.Message);
        }
        catch (Eingabefehler error)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status422UnprocessableEntity,
                "Request failed", error.Message);
        }

        _ = cancellationToken;
    }

    private static bool Firma(ICurrentPrincipal akteur, out TenantId company)
    {
        if (akteur.Current?.Acting is Capacity.ForCompany fuer)
        {
            company = fuer.Tenant;
            return true;
        }

        company = TenantId.None;
        return false;
    }

    private static Task KeineFirma(HttpContext context, ICurrentPrincipal akteur) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden, "Request failed", "no active company");

    private static JobDetails Angaben(WriteJobV1 koerper) => new(
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
            roh, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static EmploymentKind Anstellung(string? roh) => roh?.Trim().ToLowerInvariant() switch
    {
        "part_time" => EmploymentKind.PartTime,
        "contract" => EmploymentKind.Contract,
        "internship" => EmploymentKind.Internship,
        _ => EmploymentKind.FullTime
    };

    private static JobV1 Antwort(Job job) => new(
        job.Id,
        job.Firma.Value,
        job.Titel,
        job.Beschreibung,
        job.Ort,
        job.Postleitzahl,
        job.Remote.ToString().ToLowerInvariant(),
        job.Art switch
        {
            EmploymentKind.PartTime => "part_time",
            EmploymentKind.Contract => "contract",
            EmploymentKind.Internship => "internship",
            _ => "full_time"
        },
        job.Faehigkeiten.Werte,
        job.State.ToString().ToLowerInvariant(),
        job.VeroeffentlichtAm,
        job.GeaendertAm);

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
