using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Application.Connections;
using WorkerTransfer.GitHub.Contracts;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Api;

/// <summary><c>/github/*</c> — beanspruchen, beweisen, zeigen, trennen.</summary>
/// <remarks>
/// None Route hier trägt einen Punktwert, einen Rang oder eine abgeleitete
/// Fähigkeit. Was hinausgeht, sind Belege mit Links: was GitHub über ein
/// <em>Repository</em> sagt. Eine Aussage über einen <em>Menschen</em> wäre ein
/// Urteil, das er nicht kommentieren kann (ADR-0022).
/// </remarks>
public static class ConnectionEndpoints
{
    /// <summary>Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.</summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// ResumeRequest mit Rumpf reißt dabei die Connection.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Bildet die Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapVerbindungsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var github = app.MapGroup("/github");

        // Zwei stille Abhängigkeiten, zwei verschiedene Sätze — aber derselbe
        // Statuscode. Weder GitHub noch der Ledger darf zu einer Behauptung
        // über die Person werden: „nicht bewiesen", weil wir nicht fragen
        // konnten, wäre eine.
        github.AddEndpointFilter(async (call, weiter) =>
        {
            try
            {
                return await weiter(call);
            }
            catch (GitHubSignInSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "github unavailable");

                return Geschrieben;
            }
            catch (ConsentGateSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the consent ledger did not answer");

                return Geschrieben;
            }
        });

        // Den Benutzernamen nennen und die Einmalzeichenfolge bekommen. Hier
        // wird NICHT bei GitHub angefragt: solange nichts bewiesen ist, gibt es
        // nichts zu holen — und ein Abruf verriete nur, dass jemand nach diesem
        // Konto gefragt hat.
        github.MapPost("/me", async (
            ConnectV1 body,
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

            var result = await mediator.Send(
                new ConnectCommand(handelnder.Subject, body.Login), cancellationToken);

            await Beantworte(context, result, eigen: true, cancellationToken);
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

            var result = await mediator.Send(
                new ProveCommand(handelnder.Subject), cancellationToken);

            await Beantworte(context, result, eigen: true, cancellationToken);
        });

        // DER WEG ÜBER GITHUBS EIGENE ANMELDUNG — er ersetzt den Gist, nicht
        // die Nennung. Ohne hinterlegte Zugangsdaten antwortet er mit `null`,
        // und die Oberfläche bietet weiter den Gist an.
        github.MapPost("/me/oauth/start", async (
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

            var beginn = await mediator.Send(
                new BeginSignInCommand(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new AnmeldebeginnV1(beginn.Adresse?.ToString()), cancellationToken);
        });

        // OHNE `/me`, WEIL NICHTS PERSÖNLICHES DARIN STEHT: die Frage lautet,
        // ob dieser Server die Anmeldung eingerichtet hat. Sie legt nichts an —
        // im Gegensatz zu `/me/oauth/start`, das genau deshalb erst auf
        // Knopfdruck gerufen wird und nicht beim Betrachten der Seite.
        github.MapGet("/oauth", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // Angemeldet, obwohl nichts Persönliches darin steht: die Antwort
            // hilft nur beim Verbinden, und das kann ohnehin nur, wer
            // angemeldet ist. Eine neue offene Tür ohne Grund ist eine zu viel.
            if (akteur.Current is null)
            {
                await NichtAngemeldet(context);
                return;
            }

            var moeglich = await mediator.Send(
                new SignInPossibleQuery(), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new SignInPossibleV1(moeglich), cancellationToken);
        });

        github.MapPost("/me/oauth/finish", async (
            CompleteSignInV1 koerper,
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
                new CompleteSignInCommand(
                    handelnder.Subject, koerper.Code, koerper.State),
                cancellationToken);

            await Beantworte(context, result, eigen: true, cancellationToken);
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

            var result = await mediator.Send(
                new AuffrischenBefehl(handelnder.Subject), cancellationToken);

            await Beantworte(context, result, eigen: true, cancellationToken);
        });

        // Auch die unbewiesene Connection — sonst sähe die Person nach dem
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

            var connection = await mediator.Send(
                new MyConnectionQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                connection is null ? null : Antwort(connection, eigen: true), cancellationToken);
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

            var connection = await mediator.Send(
                new VisibleConnectionQuery(new SubjectId(subjectId)), cancellationToken);

            if (connection is null)
            {
                // Nicht vorhanden, nicht bewiesen, nicht freigegeben —
                // dieselbe Antwort. Eine unbewiesene Connection ist eine
                // Behauptung, und Behauptungen zeigt dieser Dienst nicht.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;
            }

            await context.Response.WriteAsJsonAsync(
                Antwort(connection, eigen: false), cancellationToken);
        });

        return app;
    }

    private static async Task Beantworte(
        HttpContext context,
        ConnectionResult result,
        bool eigen,
        CancellationToken cancellationToken)
    {
        switch (result)
        {
            case ConnectionResult.Erledigt erledigt:
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Connection, eigen), cancellationToken);
                return;

            case ConnectionResult.None:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such connection");
                return;

            case ConnectionResult.NichtBewiesen:
                // 422, nicht 404: die ResumeRequest war in Ordnung, der Nachweis
                // fehlte.
                //
                // Die Meldung nennt den WEG nicht mehr. Sie lautete „no public
                // gist with that description was found" — richtig, solange es
                // nur den Gist gab, und irreführend, seit die Anmeldung über
                // GitHub daneben steht: wer dort ein fremdes Konto autorisiert,
                // hätte gelesen, sein Gist fehle.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "the account was not proven");
                return;

            case ConnectionResult.Eingabe eingabe:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;

            default:
                throw new InvalidOperationException($"Unbehandeltes Ergebnis: {result}");
        }
    }

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static ConnectionV1 Antwort(Connection connection, bool eigen) =>
        new(connection.Wer.Value,
            connection.Login,
            connection.Nachgewiesen,
            // Nur in der eigenen Ansicht, und nur solange nicht bewiesen: die
            // Einmalzeichenfolge nützt allein der Person, die den Gist anlegt.
            eigen && !connection.Nachgewiesen
                ? Connection.Gistbeschreibung(connection.Einmalzeichenfolge)
                : null,
            connection.GeholtAm,
            [
                .. connection.Repositories.Select(entry => new RepositoryV1(
                    entry.Name, entry.Beschreibung, entry.Sprache,
                    entry.Sterne, entry.Adresse, entry.ZuletztGeschoben,
                    entry.Sprachen, entry.Themen))
            ],
            connection.SprachenVollstaendig);
}
