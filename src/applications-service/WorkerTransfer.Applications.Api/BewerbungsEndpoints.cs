using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Applications;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary>Die sechs Routen der JobApplication.</summary>
/// <remarks>
/// None trägt einen Punktwert, einen Rang oder eine Sortierung nach Passung.
/// Wie gut jemand zu einer Job passt, wird im Browser gerechnet, der
/// <em>Person</em> gezeigt und nie dem Unternehmen (ADR-0022); eine
/// Bewerberliste, die dieser Dienst reihte, wäre die Kandidatenbewertung durch
/// die Hintertür.
/// </remarks>
public static class ApplicationEndpoints
{
    /// <summary>Bildet alle Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapBewerbungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var applications = Abgesichert(app.MapGroup("/applications"));
        var jobs = Abgesichert(app.MapGroup("/jobs"));
        var companies = Abgesichert(app.MapGroup("/companies"));

        // Bewerben — und damit die Daten für dieses eine Unternehmen freigeben.
        // Die Release entsteht im Ledger, nicht in dieser Datenbank, und sie
        // nennt den Empfänger.
        applications.MapPost("/", async (
            SubmitApplicationV1 body,
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

            ArgumentNullException.ThrowIfNull(body);

            // Wer sich bewirbt, steht im geprüften Token und nie im Rumpf.
            var result = await mediator.Send(
                new SubmitApplicationCommand(
                    body.JobId, handelnder.Subject, body.Message,
                    body.SharesResume, body.SharesPortfolio),
                cancellationToken);

            await Beantworte(context, result, cancellationToken, StatusCodes.Status201Created);
        });

        applications.MapGet("/me", async (
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
                new MyApplicationsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort), cancellationToken);
        });

        applications.MapGet("/{id:guid}", async (
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

            TenantId? company = handelnder.Acting is Capacity.ForCompany fuer
                ? fuer.Tenant
                : null;

            var application = await mediator.Send(
                new ReadApplicationQuery(id, company, handelnder.Subject),
                cancellationToken);

            if (application is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such application");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(application), cancellationToken);
        });

        // Zurückziehen — der Case bleibt, die Daten sind weg. Dass jemand
        // sich beworben und zurückgezogen hat, gehört zur Geschichte des
        // Verfahrens im Unternehmen; die Person dahinter ist danach nicht mehr
        // einsehbar.
        applications.MapPost("/{id:guid}/withdraw", async (
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
                new WithdrawApplicationCommand(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, result, cancellationToken);
        });

        applications.MapPost("/{id:guid}/status", async (
            Guid id,
            MoveApplicationV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var result = await mediator.Send(
                new MoveApplicationCommand(id, company, body.Status), cancellationToken);

            await Beantworte(context, result, cancellationToken);
        });

        jobs.MapGet("/{job:guid}/applications", async (
            Guid job,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } company)
            {
                return;
            }

            var found = await mediator.Send(
                new ApplicationsForJobQuery(job, company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                found.Select(Antwort), cancellationToken);
        });

        // Zahlen über die EIGENEN Vorgänge. Das Unternehmen sieht sie ohnehin
        // einzeln; die Summe darüber ist eine Bequemlichkeit, keine neue
        // Auskunft. Was es hier nicht gibt und nicht geben soll, ist die
        // Zusammenführung mit MarketStatus, CurriculumVitae oder Vorgängen bei
        // anderen Firmen (ADR-0022/0026).
        companies.MapGet("/me/application-stats", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } company)
            {
                return;
            }

            var zahlen = await mediator.Send(new CompanyCountsQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new ApplicationCountsV1(
                    zahlen.ToDictionary(
                        entry => ApplicationStates.Wort(entry.Key),
                        entry => entry.Value),
                    zahlen.Values.Sum()),
                cancellationToken);
        });

        return app;
    }

    /// <summary>
    /// Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.
    /// </summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk die Antwort noch einmal schreiben — als
    /// JSON-<c>null</c>, mitsamt Kopfzeilen, und die stehen zu diesem Zeitpunkt
    /// schon. Was dabei herauskommt, ist keine 500, sondern eine abgerissene
    /// Connection: der Aufrufer bekommt „Error while copying content to a
    /// stream" und nie das 503, das ihm zusteht. Gemessen an genau dieser
    /// Route, die zwei Tage lang wie ein kaputter Rumpf aussah.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Hängt die 503-Übersetzung an eine Gruppe.</summary>
    /// <remarks>
    /// Ein schweigender Ledger — und ein schweigender Jobs-Dienst — sind weder
    /// ein Ja noch ein Nein. Mit 404 zu antworten hieße zu behaupten, es gäbe
    /// die Job nicht; die JobApplication trotzdem anzulegen hieße, eine Release
    /// zu unterstellen, die niemand bestätigt hat. 503 sagt das eine Wahre:
    /// dieser Dienst kann gerade nicht antworten.
    /// <para>
    /// Als Filter je Gruppe statt als <c>catch</c> je Route — und an
    /// <em>allen</em> Gruppen, auch denen, die heute keinen der beiden Dienste
    /// anfassen. Die Route, die es vergisst, sieht aus wie eine funktionierende,
    /// bis eine Abhängigkeit ausfällt.
    /// </para>
    /// </remarks>
    private static RouteGroupBuilder Abgesichert(RouteGroupBuilder group)
    {
        group.AddEndpointFilter(async (call, weiter) =>
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
            catch (JobLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a dependency is unavailable");

                return Geschrieben;
            }
            catch (CompanyLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "identity-service did not answer");

                return Geschrieben;
            }
        });

        return group;
    }

    /// <summary>Die Firma des Aufrufers, oder eine schon geschriebene Absage.</summary>
    private static async Task<TenantId?> Firma(HttpContext context, ICurrentPrincipal akteur)
    {
        if (akteur.Current is not { } handelnder)
        {
            await NichtAngemeldet(context);
            return null;
        }

        if (handelnder.Acting is not Capacity.ForCompany company)
        {
            // Eine Aussage über den Aufrufer, die nichts verrät: Applications
            // liest, wer für ein Unternehmen handelt, und eine Privatperson
            // weiß, dass sie keines ist.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "reading applications requires an active company");

            return null;
        }

        return company.Tenant;
    }

    /// <summary>
    /// Übersetzt die Responses der Domäne in Statuscodes — an einer Job,
    /// damit vier Routen nicht auseinanderdriften.
    /// </summary>
    private static async Task Beantworte(
        HttpContext context,
        ApplicationResult result,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        switch (result)
        {
            case ApplicationResult.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.JobApplication), cancellationToken);
                return;

            case ApplicationResult.NoJob:
                // Entwurf, geschlossen und nicht vorhanden bleiben
                // ununterscheidbar — so hält es der Jobs-Dienst selbst.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such job");
                return;

            case ApplicationResult.Unbekannt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such application");
                return;

            case ApplicationResult.StateConflict konflikt:
                // Die Eingabe ist in Ordnung, der Zustand passt nicht.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            case ApplicationResult.Eingabe eingabe:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {result}");
        }
    }

    private static ApplicationV1 Antwort(JobApplication application) =>
        new(application.Id,
            application.Job,
            application.Firma.Value,
            application.Wer.Value,
            application.Nachricht,
            application.Mitgeschickt.CurriculumVitae,
            application.Mitgeschickt.Portfolio,
            application.Mitgeschickt.Unterlagen,
            ApplicationStates.Wort(application.State),
            application.AngelegtAm,
            application.GeaendertAm,
            new ApplicantContactV1(
                application.Kontakt.Klarname,
                application.Kontakt.Zeile1,
                application.Kontakt.Zeile2,
                application.Kontakt.Postleitzahl,
                application.Kontakt.Ort,
                application.Kontakt.Land,
                application.Kontakt.Telefon,
                application.Kontakt.Email));

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
