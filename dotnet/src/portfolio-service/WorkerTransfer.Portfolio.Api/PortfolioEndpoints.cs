using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Portfolio.Application.Ports;
using WorkerTransfer.Portfolio.Application.Portfolios;
using WorkerTransfer.Portfolio.Contracts;
using WorkerTransfer.Portfolio.Domain.Portfolios;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Portfolio.Api;

/// <summary><c>/portfolios/*</c> — schreiben, lesen, Arbeitsproben.</summary>
public static class PortfolioEndpoints
{
    /// <summary>Wie groß eine hochgeladene Datei sein darf.</summary>
    private const long HoechsteGroesse = 10 * 1024 * 1024;

    /// <summary>Bindet die fünf Portfoliorouten ein.</summary>
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ein schweigender Ledger ist weder ein Ja noch ein Nein. Als ein
        // Filter für die ganze Gruppe, weil die Route, die es vergisst, wie
        // eine funktionierende aussieht, bis der Ledger ausfällt.
        var portfolios = app.MapGroup("/portfolios").AddEndpointFilter(
            async (aufruf, weiter) =>
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

        portfolios.MapPut("/me", async (
            PortfolioSchreibenV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            TimeProvider uhr,
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
                var portfolio = await mediator.Send(
                    new PortfolioSichernBefehl(handelnder.Subject, Eintraege(koerper, uhr)),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(portfolio), cancellationToken);
            }
            catch (Eingabefehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        portfolios.MapGet("/me", async (
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

            // Ohne Ledgerfrage: die Freigabe regelt, wer es von außen sieht,
            // nicht ob jemand sein eigenes lesen darf.
            var portfolio = await mediator.Send(
                new MeinPortfolioAbfrage(handelnder.Subject), cancellationToken);

            if (portfolio is null)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no portfolio yet");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(portfolio), cancellationToken);
        });

        portfolios.MapGet("/{subjectId:guid}", async (
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

            var portfolio = await mediator.Send(
                new FremdesPortfolioAbfrage(new SubjectId(subjectId)), cancellationToken);

            if (portfolio is null)
            {
                // Verborgen und nicht vorhanden antworten gleich, und das muss
                // so bleiben: ein Unterschied sagte, ob dieser Mensch hier
                // etwas gezeigt hat.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such portfolio");
                return;
            }

            await context.Response.WriteAsJsonAsync(Antwort(portfolio), cancellationToken);
        });

        portfolios.MapPost("/me/attachments", async (
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

            if (!context.Request.HasFormContentType)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status415UnsupportedMediaType,
                    "Request failed", "expected a multipart upload");
                return;
            }

            var formular = await context.Request.ReadFormAsync(cancellationToken);
            var datei = formular.Files.GetFile("file");

            if (datei is null || datei.Length == 0)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "no file");
                return;
            }

            // Eine Grenze, weil ohne sie die erste Videodatei den Datenträger
            // füllt — und weil ein Portfolio ein Schaufenster ist, kein Archiv.
            if (datei.Length > HoechsteGroesse)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status413PayloadTooLarge,
                    "Request failed", "the file is too large");
                return;
            }

            await using var inhalt = datei.OpenReadStream();

            // Der Name kommt von der Ablage zurück, nicht vom Client: ein vom
            // Aufrufer gewählter Name wäre ein Pfad, den jemand formen kann.
            var name = await mediator.Send(
                new AnhangAblegenBefehl(
                    handelnder.Subject, datei.FileName, datei.ContentType, inhalt),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, string> { ["attachment"] = name }, cancellationToken);
        });

        portfolios.MapGet("/{subjectId:guid}/attachments/{name}", async (
            Guid subjectId,
            string name,
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

            var abgelegtes = await mediator.Send(
                new AnhangAbfrage(new SubjectId(subjectId), name, handelnder.Subject),
                cancellationToken);

            if (abgelegtes is null)
            {
                // Dieselbe Antwort, ob die Datei fehlt, das Portfolio verborgen
                // ist oder kein Eintrag sie nennt. Drei Gründe, eine Antwort.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such attachment");
                return;
            }

            await using (abgelegtes)
            {
                context.Response.ContentType = abgelegtes.Medientyp;

                // Kein Anzeigen im Browser: eine hochgeladene Datei, die ein
                // fremder Mensch öffnet, wird heruntergeladen und nicht in
                // unserem Ursprung ausgeführt.
                context.Response.Headers.ContentDisposition = $"attachment; filename=\"{name}\"";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";

                await abgelegtes.Inhalt.CopyToAsync(context.Response.Body, cancellationToken);
            }
        });

        return app;
    }

    private static IReadOnlyList<Eintrag> Eintraege(
        PortfolioSchreibenV1 koerper, TimeProvider uhr)
    {
        var jetzt = uhr.GetUtcNow();

        return [.. (koerper.Items ?? []).Select(eintrag => Eintrag.Aus(
            eintrag.Title, jetzt, eintrag.Summary, eintrag.Url,
            eintrag.Role, eintrag.Year, eintrag.Attachment))];
    }

    private static PortfolioV1 Antwort(Domain.Portfolios.Portfolio portfolio) => new(
        portfolio.Wer.Value,
        [.. portfolio.Eintraege.Select(eintrag => new EintragV1(
            eintrag.Titel, eintrag.Zusammenfassung, eintrag.Link,
            eintrag.Rolle, eintrag.Jahr, eintrag.Anhang))],
        portfolio.GeaendertAm);

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
