using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Applications.Application.Entwuerfe;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Contracts;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Api;

/// <summary><c>/applications/drafts/*</c> — schreiben, prüfen, freigeben, senden.</summary>
/// <remarks>
/// <strong>Es gibt keinen Weg von „entsteht" nach „gesendet" ohne zwei Klicks
/// eines Menschen</strong> (ADR-0034). Die Massenhandlung liegt beim Anlegen,
/// wo sie nichts anrichtet; am Ausgang steht sie nicht.
/// </remarks>
public static class DraftEndpoints
{
    private static readonly IResult Geschrieben = Results.Empty;

    /// <summary>Bindet die Entwurfsrouten ein.</summary>
    public static IEndpointRouteBuilder MapEntwurfsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var drafts = app.MapGroup("/applications/drafts");

        // Drei stille Abhängigkeiten, ein Statuscode: weder der Ledger noch
        // jobs-service noch ein Dienst mit den eigenen Angaben darf zu einer
        // Behauptung über die JobApplication werden.
        drafts.AddEndpointFilter(async (call, weiter) =>
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
                    "Request failed", "jobs-service did not answer");

                return Geschrieben;
            }
            catch (ApplicantLookupSilent)
            {
                await ProblemDetailsMiddleware.Write(
                    call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "a service holding your own data did not answer");

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

        drafts.MapPost("/", async (
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
            // ResumeRequest, und die Antwort ist dann eine kuerzere Liste.
            if (koerper?.JobIds is not { Count: > 0 } jobs)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "invalid: job_ids");
                return;
            }

            var angelegt = await mediator.Send(
                new EntwuerfeAnlegenBefehl(handelnder.Subject, jobs),
                cancellationToken);

            context.Response.StatusCode = StatusCodes.Status201Created;
            await context.Response.WriteAsJsonAsync(
                angelegt.Select(Antwort).ToArray(), cancellationToken);
        });

        drafts.MapPost("/{id:guid}/write", Schritt(
            (id, subject) => new WriteDraftCommand(subject, id),
            CoverLetterJobKind.Schreiben));

        drafts.MapPost("/{id:guid}/revise", Schritt(
            (id, subject) => new ReviseCommand(subject, id),
            CoverLetterJobKind.Ueberarbeiten));

        drafts.MapPost("/{id:guid}/approve", Schritt(
            (id, subject) => new FreigebenBefehl(subject, id)));

        drafts.MapPost("/{id:guid}/send", Schritt(
            (id, subject) => new SendDraftCommand(subject, id)));

        drafts.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            ICoverLetterQueue schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MyDraftsQuery(handelnder.Subject), cancellationToken);

            foreach (var offen in meine)
            {
                StarteWennEntsteht(offen, handelnder.Subject, context, schlange);
            }

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort).ToArray(), cancellationToken);
        });

        drafts.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            ICoverLetterQueue schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var draft = await mediator.Send(
                new DraftQuery(handelnder.Subject, id), cancellationToken);

            if (draft is null)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;
            }

            StarteWennEntsteht(draft, handelnder.Subject, context, schlange);

            await context.Response.WriteAsJsonAsync(Antwort(draft), cancellationToken);
        });

        drafts.MapPatch("/{id:guid}", async (
            Guid id,
            ChangeDraftV1 koerper,
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
                    new ChangeDraftCommand(
                        handelnder.Subject, id, koerper?.Subject, koerper?.Body),
                    cancellationToken),
                cancellationToken);
        });

        drafts.MapPut("/{id:guid}/attachments", async (
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

        drafts.MapPost("/{id:guid}/comments", async (
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

        drafts.MapDelete("/{id:guid}", async (
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
                new DeleteDraftCommand(handelnder.Subject, id), cancellationToken);

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
        Func<Guid, SubjectId, IRequest<DraftResult>> baue,
        CoverLetterJobKind? danach = null) =>
        async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            ICoverLetterQueue schlange,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await NichtAngemeldet(context);
                return;
            }

            var result = await mediator.Send(
                baue(id, handelnder.Subject), cancellationToken);

            await Beantworte(context, result, cancellationToken);

            if (danach is { } kind && result is DraftResult.Erledigt)
            {
                schlange.Plane(new CoverLetterJob(
                    handelnder.Subject, id, Traeger(context), kind));
            }
        };

    /// <summary>
    /// Der Worker lebt im Prozess. Nach einem Neustart ist die Schlange leer,
    /// der Entwurf steht aber weiter auf Entsteht — dann schreibt niemand, und
    /// die Oberfläche pollt einen leeren Brief. Ein GET reicht, um denselben
    /// Auftrag erneut anzustellen; läuft er schon, lehnt Plane ab.
    /// </summary>
    private static void StarteWennEntsteht(
        ApplicationDraft draft,
        SubjectId subject,
        HttpContext context,
        ICoverLetterQueue schlange)
    {
        if (draft.State != DraftState.Entsteht)
        {
            return;
        }

        if (schlange.Laeuft(draft.Id))
        {
            return;
        }

        schlange.Plane(new CoverLetterJob(
            subject,
            draft.Id,
            Traeger(context),
            draft.HatOffeneAnmerkungen
                ? CoverLetterJobKind.Ueberarbeiten
                : CoverLetterJobKind.Schreiben));
    }

    private static string? Traeger(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();

        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return header["Bearer ".Length..];
        }

        return context.Request.Cookies.TryGetValue("access", out var ausCookie)
               && !string.IsNullOrEmpty(ausCookie)
            ? ausCookie
            : null;
    }

    private static async Task Beantworte(
        HttpContext context, DraftResult result, CancellationToken cancellationToken)
    {
        switch (result)
        {
            case DraftResult.Erledigt erledigt:
                await context.Response.WriteAsJsonAsync(
                    Antwort(erledigt.Entwurf), cancellationToken);
                return;

            case DraftResult.None:
                // „Gibt es nicht" und „gehört jemand anderem" sind von außen
                // dasselbe — sonst verriete ein 403 die Existenz.
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "no such draft");
                return;

            case DraftResult.NoProvider kein:
                // 503 und nicht 500: es ist nichts kaputt, es ist nichts
                // eingerichtet — und die Oberfläche sagt das dem Menschen,
                // statt still nichts zu tun (ADR-0024).
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", kein.Grund);
                return;

            case DraftResult.StateConflict konflikt:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status409Conflict,
                    "Request failed", konflikt.Grund);
                return;

            case DraftResult.Eingabe eingabe:
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", eingabe.Grund);
                return;
        }
    }

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized,
            "Request failed", "authentication required");

    private static DraftV1 Antwort(ApplicationDraft draft) => new(
        draft.Id,
        draft.Job,
        draft.Betreff,
        draft.Text,
        EfDraftStore.Standwort(draft.State),
        draft.Fassung,
        draft.Fehler,
        draft.TeiltLebenslauf,
        draft.Unterlagen,
        [
            .. draft.Anmerkungen.Select(entry => new AnmerkungV1(
                entry.Id, entry.Text, entry.Zitat, entry.Erledigt, entry.Angelegt))
        ],
        draft.Geaendert,
        draft.SchreibenBegonnen);
}
