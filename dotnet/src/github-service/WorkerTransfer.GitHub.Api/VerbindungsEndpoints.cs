using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Application.Verbindungen;
using WorkerTransfer.GitHub.Contracts;
using WorkerTransfer.GitHub.Domain.Verbindungen;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Api;

/// <summary><c>/github/*</c> — beanspruchen, beweisen, zeigen, trennen.</summary>
/// <remarks>
/// Keine Route hier trägt einen Punktwert, einen Rang oder eine abgeleitete
/// Fähigkeit. Was hinausgeht, sind Belege mit Links: was GitHub über ein
/// <em>Repository</em> sagt. Eine Aussage über einen <em>Menschen</em> wäre ein
/// Urteil, das er nicht kommentieren kann (ADR-0022).
/// </remarks>
public static class VerbindungsEndpoints
{
    /// <summary>Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.</summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// Anfrage mit Rumpf reißt dabei die Verbindung.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Bildet die sechs Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapVerbindungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var github = app.MapGroup("/github");

        // Zwei stille Abhängigkeiten, zwei verschiedene Sätze — aber derselbe
        // Statuscode. Weder GitHub noch der Ledger darf zu einer Behauptung
        // über die Person werden: „nicht bewiesen", weil wir nicht fragen
        // konnten, wäre eine.
        github.AddEndpointFilter(async (aufruf, weiter) =>
        {
            try
            {
                return await weiter(aufruf);
            }
            catch (GitHubSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "github unavailable");

                return Geschrieben;
            }
            catch (EinwilligungSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Geschrieben;
            }
        });

        // Den Benutzernamen nennen und die Einmalzeichenfolge bekommen. Hier
        // wird NICHT bei GitHub angefragt: solange nichts bewiesen ist, gibt es
        // nichts zu holen — und ein Abruf verriete nur, dass jemand nach diesem
        // Konto gefragt hat.
        github.MapPost("/me", async (
            VerbindenV1 body,
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

            var ergebnis = await mediator.Send(
                new VerbindenBefehl(handelnder.Subject, body.Login), cancellationToken);

            await Beantworte(context, ergebnis, eigen: true, cancellationToken);
        });

        github.MapPost("/me/verify", async (
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
                new NachweisenBefehl(handelnder.Subject), cancellationToken);

            await Beantworte(context, ergebnis, eigen: true, cancellationToken);
        });

        github.MapPost("/me/refresh", async (
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
                new AuffrischenBefehl(handelnder.Subject), cancellationToken);

            await Beantworte(context, ergebnis, eigen: true, cancellationToken);
        });

        // Auch die unbewiesene Verbindung — sonst sähe die Person nach dem
        // ersten Schritt gar nichts und wüsste nicht, welche Zeichenfolge sie
        // in den Gist schreiben soll.
        github.MapGet("/me", async (
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

            var verbindung = await mediator.Send(
                new MeineVerbindungAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                verbindung is null ? null : Antwort(verbindung, eigen: true), cancellationToken);
        });

        // Trennen heißt löschen — der Abzug verschwindet mit.
        github.MapDelete("/me", async (
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

            await mediator.Send(new TrennenBefehl(handelnder.Subject), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        github.MapGet("/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is null)
            {
                await NichtAngemeldet(context);
                return;
            }

            var verbindung = await mediator.Send(
                new SichtbareVerbindungAbfrage(new SubjectId(subjectId)), cancellationToken);

            if (verbindung is null)
            {
                // Nicht vorhanden, nicht bewiesen, nicht freigegeben —
                // dieselbe Antwort. Eine unbewiesene Verbindung ist eine
                // Behauptung, und Behauptungen zeigt dieser Dienst nicht.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                Antwort(verbindung, eigen: false), cancellationToken);
        });

        return app;
    }

    private static async Task Beantworte(
        HttpContext context,
        Verbindungsergebnis ergebnis,
        bool eigen,
        CancellationToken cancellationToken)
    {
        switch (ergebnis)
        {
            case Verbindungsergebnis.Erledigt erledigt:
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Verbindung, eigen), cancellationToken);
                return;

            case Verbindungsergebnis.Keine:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;

            case Verbindungsergebnis.NichtBewiesen:
                // 422, nicht 404: die Anfrage war in Ordnung, der Nachweis
                // fehlte.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "no public gist with that description was found");
                return;

            case Verbindungsergebnis.Eingabe eingabe:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {ergebnis}");
        }
    }

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static VerbindungV1 Antwort(Verbindung verbindung, bool eigen) =>
        new(verbindung.Wer.Value,
            verbindung.Login,
            verbindung.Nachgewiesen,
            // Nur in der eigenen Ansicht, und nur solange nicht bewiesen: die
            // Einmalzeichenfolge nützt allein der Person, die den Gist anlegt.
            eigen && !verbindung.Nachgewiesen
                ? Verbindung.Gistbeschreibung(verbindung.Einmalzeichenfolge)
                : null,
            verbindung.GeholtAm,
            [
                .. verbindung.Repositories.Select(eintrag => new RepositoryV1(
                    eintrag.Name, eintrag.Beschreibung, eintrag.Sprache,
                    eintrag.Sterne, eintrag.Adresse, eintrag.ZuletztGeschoben))
            ]);
}
