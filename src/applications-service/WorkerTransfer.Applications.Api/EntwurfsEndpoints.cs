using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Bewerbungen;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary><c>/applications/drafts/*</c> — schreiben, prüfen, freigeben, senden.</summary>
/// <remarks>
/// <strong>Es gibt keinen Weg von „entsteht" nach „gesendet" ohne zwei Klicks
/// eines Menschen</strong> (ADR-0034). Die Massenhandlung liegt beim Anlegen,
/// wo sie nichts anrichtet; am Ausgang steht sie nicht.
/// </remarks>
public static class EntwurfsEndpoints
{
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Bindet die Entwurfsrouten ein.</summary>
    public static IEndpointRouteBuilder MapEntwurfsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var entwuerfe = app.MapGroup("/applications/drafts");

        // Drei stille Abhängigkeiten, ein Statuscode: weder der Ledger noch
        // jobs-service noch ein Dienst mit den eigenen Angaben darf zu einer
        // Behauptung über die Bewerbung werden.
        entwuerfe.AddEndpointFilter(async (aufruf, weiter) =>
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
                    "Request failed", "jobs-service did not answer");

                return Geschrieben;
            }
            catch (BewerberSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a service holding your own data did not answer");

                return Geschrieben;
            }
            catch (FirmaSchweigt)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    aufruf.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "identity-service did not answer");

                return Geschrieben;
            }
        });

        entwuerfe.MapPost("/", async (
            EntwuerfeAnlegenV1 koerper,
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

            // WER NACH NICHTS FRAGT, BEKOMMT KEIN 201.
            //
            // Eine leere Liste legte null Entwuerfe an und meldete trotzdem
            // „Created" — ein Statuscode, der etwas behauptet, das nicht
            // geschehen ist. Gemessen an der Routenkarte, die 422 als Absicht
            // festhielt; die Karte hatte recht.
            //
            // NICHT betroffen: eine Liste mit Kennungen, die es nicht (mehr)
            // gibt. Eine geschlossene Ausschreibung ist keine fehlerhafte
            // Anfrage, und die Antwort ist dann eine kuerzere Liste.
            if (koerper?.JobIds is not { Count: > 0 } stellen)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: job_ids");
                return;
            }

            var angelegt = await mediator.Send(
                new EntwuerfeAnlegenBefehl(handelnder.Subject, stellen),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                angelegt.Select(Antwort).ToArray(), cancellationToken);
        });

        entwuerfe.MapPost("/{id:guid}/write", Schritt(
            (id, wer) => new EntwurfSchreibenBefehl(wer, id),
            AnschreibenauftragArt.Schreiben));

        entwuerfe.MapPost("/{id:guid}/revise", Schritt(
            (id, wer) => new UeberarbeitenBefehl(wer, id),
            AnschreibenauftragArt.Ueberarbeiten));

        entwuerfe.MapPost("/{id:guid}/approve", Schritt(
            (id, wer) => new FreigebenBefehl(wer, id)));

        entwuerfe.MapPost("/{id:guid}/send", Schritt(
            (id, wer) => new EntwurfSendenBefehl(wer, id)));

        entwuerfe.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            IAnschreibenSchlange schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MeineEntwuerfeAbfrage(handelnder.Subject), cancellationToken);

            foreach (var offen in meine)
            {
                StarteWennEntsteht(offen, handelnder.Subject, context, schlange);
            }

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort).ToArray(), cancellationToken);
        });

        entwuerfe.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            IAnschreibenSchlange schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var entwurf = await mediator.Send(
                new EntwurfAbfrage(handelnder.Subject, id), cancellationToken);

            if (entwurf is null)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;
            }

            StarteWennEntsteht(entwurf, handelnder.Subject, context, schlange);

            await context.Response.WriteAsJsonAsync(Antwort(entwurf), cancellationToken);
        });

        entwuerfe.MapPatch("/{id:guid}", async (
            Guid id,
            EntwurfAendernV1 koerper,
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

            await Beantworte(
                context,
                await mediator.Send(
                    new EntwurfAendernBefehl(
                        handelnder.Subject, id, koerper?.Subject, koerper?.Body),
                    cancellationToken),
                cancellationToken);
        });

        entwuerfe.MapPut("/{id:guid}/attachments", async (
            Guid id,
            BeilagenV1 koerper,
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

            await Beantworte(
                context,
                await mediator.Send(
                    new BeilagenWaehlenBefehl(
                        handelnder.Subject, id,
                        koerper?.SharesResume ?? true,
                        koerper?.Documents ?? []),
                    cancellationToken),
                cancellationToken);
        });

        entwuerfe.MapPost("/{id:guid}/comments", async (
            Guid id,
            AnmerkenV1 koerper,
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

            await Beantworte(
                context,
                await mediator.Send(
                    new AnmerkenBefehl(handelnder.Subject, id, koerper?.Text, koerper?.Quote),
                    cancellationToken),
                cancellationToken);
        });

        entwuerfe.MapDelete("/{id:guid}", async (
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
                new EntwurfLoeschenBefehl(handelnder.Subject, id), cancellationToken);

            context.Response.StatusCode = weg
                ? StatusCodes.Status204NoContent
                : StatusCodes.Status404NotFound;
        });

        return app;
    }

    /// <summary>Ein Schritt ohne Rumpf — immer derselbe Ablauf.</summary>
    /// <remarks>
    /// Vier Routen unterscheiden sich nur im Befehl. Sie einzeln auszuschreiben
    /// wäre viermal dieselbe Gelegenheit, die Anmeldeprüfung oder eine
    /// Fehlerabbildung zu vergessen.
    /// </remarks>
    private static Delegate Schritt(
        Func<Guid, SubjectId, IRequest<Entwurfsergebnis>> baue,
        AnschreibenauftragArt? danach = null) =>
        async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            IAnschreibenSchlange schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var ergebnis = await mediator.Send(
                baue(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, ergebnis, cancellationToken);

            if (danach is { } art && ergebnis is Entwurfsergebnis.Erledigt)
            {
                schlange.Plane(new Anschreibenauftrag(
                    handelnder.Subject, id, Traeger(context), art));
            }
        };

    /// <summary>
    /// Der Worker lebt im Prozess. Nach einem Neustart ist die Schlange leer,
    /// der Entwurf steht aber weiter auf Entsteht — dann schreibt niemand, und
    /// die Oberfläche pollt einen leeren Brief. Ein GET reicht, um denselben
    /// Auftrag erneut anzustellen; läuft er schon, lehnt Plane ab.
    /// </summary>
    private static void StarteWennEntsteht(
        Bewerbungsentwurf entwurf,
        SubjectId wer,
        HttpContext context,
        IAnschreibenSchlange schlange)
    {
        if (entwurf.Stand != Entwurfsstand.Entsteht)
        {
            return;
        }

        if (schlange.Laeuft(entwurf.Id))
        {
            return;
        }

        schlange.Plane(new Anschreibenauftrag(
            wer,
            entwurf.Id,
            Traeger(context),
            entwurf.HatOffeneAnmerkungen
                ? AnschreibenauftragArt.Ueberarbeiten
                : AnschreibenauftragArt.Schreiben));
    }

    private static string? Traeger(HttpContext context)
    {
        var kopf = context.Request.Headers.Authorization.ToString();

        if (kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return kopf["Bearer ".Length..];
        }

        return context.Request.Cookies.TryGetValue("access", out var ausCookie)
               && !string.IsNullOrEmpty(ausCookie)
            ? ausCookie
            : null;
    }

    private static async Task Beantworte(
        HttpContext context, Entwurfsergebnis ergebnis, CancellationToken cancellationToken)
    {
        switch (ergebnis)
        {
            case Entwurfsergebnis.Erledigt erledigt:
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Entwurf), cancellationToken);
                return;

            case Entwurfsergebnis.Keiner:
                // „Gibt es nicht" und „gehört jemand anderem" sind von außen
                // dasselbe — sonst verriete ein 403 die Existenz.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;

            case Entwurfsergebnis.KeinAnbieter kein:
                // 503 und nicht 500: es ist nichts kaputt, es ist nichts
                // eingerichtet — und die Oberfläche sagt das dem Menschen,
                // statt still nichts zu tun (ADR-0024).
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", kein.Grund);
                return;

            case Entwurfsergebnis.Zustandskonflikt konflikt:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", konflikt.Grund);
                return;

            case Entwurfsergebnis.Eingabe eingabe:
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;
        }
    }

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized,
            "Request failed", "authentication required");

    private static EntwurfV1 Antwort(Bewerbungsentwurf entwurf) => new(
        entwurf.Id,
        entwurf.Stelle,
        entwurf.Betreff,
        entwurf.Text,
        EfEntwurfsspeicher.Standwort(entwurf.Stand),
        entwurf.Fassung,
        entwurf.Fehler,
        entwurf.TeiltLebenslauf,
        entwurf.Unterlagen,
        [
            .. entwurf.Anmerkungen.Select(eintrag => new AnmerkungV1(
                eintrag.Id, eintrag.Text, eintrag.Zitat, eintrag.Erledigt, eintrag.Angelegt))
        ],
        entwurf.Geaendert,
        entwurf.SchreibenBegonnen);
}
