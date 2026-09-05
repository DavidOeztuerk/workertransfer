using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Bewerbungen;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary>Die sechs Routen der Bewerbung.</summary>
/// <remarks>
/// Keine trägt einen Punktwert, einen Rang oder eine Sortierung nach Passung.
/// Wie gut jemand zu einer Stelle passt, wird im Browser gerechnet, der
/// <em>Person</em> gezeigt und nie dem Unternehmen (ADR-0022); eine
/// Bewerberliste, die dieser Dienst reihte, wäre die Kandidatenbewertung durch
/// die Hintertür.
/// </remarks>
public static class BewerbungsEndpoints
{
    /// <summary>Bildet alle Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapBewerbungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var bewerbungen = Abgesichert(app.MapGroup("/applications"));
        var stellen = Abgesichert(app.MapGroup("/jobs"));
        var firmen = Abgesichert(app.MapGroup("/companies"));

        // Bewerben — und damit die Daten für dieses eine Unternehmen freigeben.
        // Die Freigabe entsteht im Ledger, nicht in dieser Datenbank, und sie
        // nennt den Empfänger.
        bewerbungen.MapPost("/", async (
            BewerbungAbschickenV1 body,
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
            var ergebnis = await mediator.Send(
                new BewerbungAbschickenBefehl(
                    body.JobId, handelnder.Subject, body.Message,
                    body.SharesResume, body.SharesPortfolio),
                cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken, StatusCodes.Status201Created);
        });

        bewerbungen.MapGet("/me", async (
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
                new MeineBewerbungenAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort), cancellationToken);
        });

        // Zurückziehen — der Vorgang bleibt, die Daten sind weg. Dass jemand
        // sich beworben und zurückgezogen hat, gehört zur Geschichte des
        // Verfahrens im Unternehmen; die Person dahinter ist danach nicht mehr
        // einsehbar.
        bewerbungen.MapPost("/{id:guid}/withdraw", async (
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
                new BewerbungZurueckziehenBefehl(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);
        });

        bewerbungen.MapPost("/{id:guid}/status", async (
            Guid id,
            BewerbungBewegenV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } firma)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var ergebnis = await mediator.Send(
                new BewerbungBewegenBefehl(id, firma, body.Status), cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);
        });

        stellen.MapGet("/{stelle:guid}/applications", async (
            Guid stelle,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var gefunden = await mediator.Send(
                new BewerbungenZurStelleAbfrage(stelle, firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                gefunden.Select(Antwort), cancellationToken);
        });

        // Zahlen über die EIGENEN Vorgänge. Das Unternehmen sieht sie ohnehin
        // einzeln; die Summe darüber ist eine Bequemlichkeit, keine neue
        // Auskunft. Was es hier nicht gibt und nicht geben soll, ist die
        // Zusammenführung mit Marktstatus, Lebenslauf oder Vorgängen bei
        // anderen Firmen (ADR-0022/0026).
        firmen.MapGet("/me/application-stats", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var zahlen = await mediator.Send(new FirmenzahlenAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new BewerbungszahlenV1(
                    zahlen.ToDictionary(
                        eintrag => Bewerbungsstaende.Wort(eintrag.Key),
                        eintrag => eintrag.Value),
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
    /// Verbindung: der Aufrufer bekommt „Error while copying content to a
    /// stream" und nie das 503, das ihm zusteht. Gemessen an genau dieser
    /// Route, die zwei Tage lang wie ein kaputter Rumpf aussah.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Hängt die 503-Übersetzung an eine Gruppe.</summary>
    /// <remarks>
    /// Ein schweigender Ledger — und ein schweigender Jobs-Dienst — sind weder
    /// ein Ja noch ein Nein. Mit 404 zu antworten hieße zu behaupten, es gäbe
    /// die Stelle nicht; die Bewerbung trotzdem anzulegen hieße, eine Freigabe
    /// zu unterstellen, die niemand bestätigt hat. 503 sagt das eine Wahre:
    /// dieser Dienst kann gerade nicht antworten.
    /// <para>
    /// Als Filter je Gruppe statt als <c>catch</c> je Route — und an
    /// <em>allen</em> Gruppen, auch denen, die heute keinen der beiden Dienste
    /// anfassen. Die Route, die es vergisst, sieht aus wie eine funktionierende,
    /// bis eine Abhängigkeit ausfällt.
    /// </para>
    /// </remarks>
    private static RouteGroupBuilder Abgesichert(RouteGroupBuilder gruppe)
    {
        gruppe.AddEndpointFilter(async (aufruf, weiter) =>
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
            catch (StelleSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a dependency is unavailable");

                return Geschrieben;
            }
        });

        return gruppe;
    }

    /// <summary>Die Firma des Aufrufers, oder eine schon geschriebene Absage.</summary>
    private static async Task<TenantId?> Firma(HttpContext context, ICurrentPrincipal akteur)
    {
        if (akteur.Current is not { } handelnder)
        {
            await NichtAngemeldet(context);
            return null;
        }

        if (handelnder.Acting is not Capacity.ForCompany firma)
        {
            // Eine Aussage über den Aufrufer, die nichts verrät: Bewerbungen
            // liest, wer für ein Unternehmen handelt, und eine Privatperson
            // weiß, dass sie keines ist.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "reading applications requires an active company");

            return null;
        }

        return firma.Tenant;
    }

    /// <summary>
    /// Übersetzt die Antworten der Domäne in Statuscodes — an einer Stelle,
    /// damit vier Routen nicht auseinanderdriften.
    /// </summary>
    private static async Task Beantworte(
        HttpContext context,
        Bewerbungsergebnis ergebnis,
        CancellationToken cancellationToken,
        int erfolg = StatusCodes.Status200OK)
    {
        switch (ergebnis)
        {
            case Bewerbungsergebnis.Erledigt erledigt:
                context.Response.StatusCode = erfolg;
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Bewerbung), cancellationToken);
                return;

            case Bewerbungsergebnis.KeineStelle:
                // Entwurf, geschlossen und nicht vorhanden bleiben
                // ununterscheidbar — so hält es der Jobs-Dienst selbst.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "No such job");
                return;

            case Bewerbungsergebnis.Unbekannt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such application");
                return;

            case Bewerbungsergebnis.Zustandskonflikt konflikt:
                // Die Eingabe ist in Ordnung, der Zustand passt nicht.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict, "Request failed", konflikt.Grund);
                return;

            case Bewerbungsergebnis.Eingabe eingabe:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {ergebnis}");
        }
    }

    private static BewerbungV1 Antwort(Bewerbung bewerbung) =>
        new(bewerbung.Id,
            bewerbung.Stelle,
            bewerbung.Firma.Value,
            bewerbung.Wer.Value,
            bewerbung.Nachricht,
            bewerbung.Mitgeschickt.Lebenslauf,
            bewerbung.Mitgeschickt.Portfolio,
            bewerbung.Mitgeschickt.Unterlagen,
            Bewerbungsstaende.Wort(bewerbung.Stand),
            bewerbung.AngelegtAm,
            bewerbung.GeaendertAm);

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
