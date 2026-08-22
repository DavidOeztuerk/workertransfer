using Girder.Core.Identity;
using MediatR;
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

                return null;
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

        return app;
    }

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
            station.Ende?.ToString(), station.Beschreibung))],
        [.. lebenslauf.Ausbildungen.Select(ausbildung => new AusbildungV1(
            ausbildung.Einrichtung, ausbildung.Abschluss, ausbildung.Beginn.ToString(),
            ausbildung.Ende?.ToString()))],
        lebenslauf.Geaendert);

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
            station.Description))];

    private static IReadOnlyList<Ausbildung> Ausbildungen(LebenslaufSpeichernV1 body) =>
        [.. body.Education.Select(ausbildung => Ausbildung.Aus(
            ausbildung.Institution, ausbildung.Qualification, Monat.Lies(ausbildung.StartedOn),
            ausbildung.EndedOn is null ? null : Monat.Lies(ausbildung.EndedOn)))];

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
