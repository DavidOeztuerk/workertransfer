using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Gespraeche;
using WorkerTransfer.Advisor.Application.Mandates;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Contracts;
using WorkerTransfer.Advisor.Domain.Gespraeche;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Api;

/// <summary><c>/advisor</c> — das Mandate, die Gespräche und ihre Stufen.</summary>
/// <remarks>
/// <para><strong>Zwei Seiten, getrennt in der Adresse.</strong> Unter
/// <c>/advisor/me/…</c> handelt ein Mensch für sich selbst; unter
/// <c>/advisor/conversations</c> handelt jemand für ein Unternehmen. Ein
/// gemeinsamer Endpunkt müsste bei jedem Aufruf herausfinden, wer gerade was
/// darf — getrennt steht es im Pfad.</para>
///
/// <para><strong><c>member</c> genügt, kein <c>admin</c>.</strong> Ein Gespräch
/// zu führen ist die tägliche Arbeit im Namen der Firma. Der eine Schritt, der
/// das Unternehmen wirklich bindet, ist die Übergabe — und der landet bei
/// transfer-service, wo <c>POST /transfers/{id}/offer</c> seit PBI-2 einen
/// <c>admin</c> verlangt. Ein zweites Recht davor machte aus einer Einladung
/// eine Zuschauerkarte.</para>
/// </remarks>
public static class BeraterEndpoints
{
    /// <summary>Bindet die Berater-Routen ein.</summary>
    public static IEndpointRouteBuilder MapBeraterEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Ein schweigender Ledger ist weder ein Ja noch ein Nein, und eine
        // schweigende Auskunft auch nicht. 404 sagte, es gebe nichts; eine
        // leere Liste behauptete dasselbe leiser. 503 sagt das einzig Wahre:
        // dieser Dienst kann gerade nicht antworten (ADR-0020 §3).
        //
        // Als ein Filter statt als Fang in jeder Route, weil die Route, die ihn
        // vergisst, wie eine funktionierende aussieht, bis etwas ausfaellt.
        var berater = app.MapGroup("/advisor").AddEndpointFilter(Abfangen);

        MapMandat(berater);
        MapMeineGespraeche(berater);
        MapFirmengespraeche(berater);

        return app;
    }

    // -----------------------------------------------------------------------
    // Das Mandate — die vier eigenen Werte
    // -----------------------------------------------------------------------

    private static void MapMandat(IEndpointRouteBuilder berater)
    {
        // Nie `null` und nie 404: „nichts gesagt" IST ein Zustand. Ein `null`
        // zwaenge die Oberflaeche, sich eine Vorgabe auszudenken — und die
        // Gefahr ist, dass sie sich die falsche ausdenkt.
        berater.MapGet("/me/mandate", async (
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

            var mandate = await mediator.Send(
                new MyMandateQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(Antwort(mandate), cancellationToken);
        });

        berater.MapPut("/me/mandate", async (
            WriteMandateV1? koerper,
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
                // Wessen Mandate es ist, kommt aus dem geprueften Token und nie
                // aus dem Rumpf: eine Subjekt-Kennung auf der Leitung waere ein
                // Weg, das Mandate eines anderen zu schreiben.
                var mandate = await mediator.Send(
                    new WriteMandateCommand(
                        handelnder.Subject,
                        koerper?.EntryMonth,
                        koerper?.SalaryMin,
                        koerper?.SalaryMax,
                        koerper?.WorkloadPercent,
                        koerper?.ExcludedDomains),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(mandate), cancellationToken);
            }
            catch (Eingabefehler error)
            {
                await Unbrauchbar(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------
    // Die Seite der Person
    // -----------------------------------------------------------------------

    private static void MapMeineGespraeche(IEndpointRouteBuilder berater)
    {
        berater.MapGet("/me/conversations", async (
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
                new MyConversationsQuery(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new MyConversationListV1([.. meine.Select(Meines)]), cancellationToken);
        });

        // Eine Stufe freigeben. Der Ledger bekommt die Ereignisse; hier wird
        // KEINE Stufe gespeichert (ADR-0037 Entscheidung 2).
        berater.MapPost("/me/conversations/{id:guid}/advance", (
                Guid id,
                StufeV1? koerper,
                IMediator mediator,
                ICurrentPrincipal akteur,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Stufenzug(
                id, koerper, mediator, akteur, context, cancellationToken,
                (conversation, subject, stage) => new StufeFreigebenBefehl(conversation, subject, stage),
                // Ohne Angabe die kleinste: eine Release soll nicht
                // versehentlich die weiteste sein.
                Stufe.Profile));

        // Und sie zurueckzunehmen. Ohne Angabe ALLES — wer widerruft, meint im
        // Zweifel das Ganze, und eine Vorgabe, die nur die oberste Stufe naehme,
        // liesse den Rest stehen, ohne dass es jemand merkt.
        berater.MapPost("/me/conversations/{id:guid}/withdraw", (
                Guid id,
                StufeV1? koerper,
                IMediator mediator,
                ICurrentPrincipal akteur,
                HttpContext context,
                CancellationToken cancellationToken) =>
            Stufenzug(
                id, koerper, mediator, akteur, context, cancellationToken,
                (conversation, subject, stage) => new RevokeStageCommand(conversation, subject, stage),
                Stufe.Profile));

        berater.MapPost("/me/conversations/{id:guid}/agree", async (
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

            try
            {
                var ansicht = await mediator.Send(
                    new ZustimmenBefehl(id, handelnder.Subject), cancellationToken);

                await context.Response.WriteAsJsonAsync(Meines(ansicht), cancellationToken);
            }
            catch (UebergangNichtErlaubt error)
            {
                await Konflikt(context, error.Message);
            }
        });

        berater.MapPost("/me/conversations/{id:guid}/end", async (
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

            try
            {
                var ansicht = await mediator.Send(
                    new BeendenBefehl(id, handelnder.Subject, null), cancellationToken);

                await context.Response.WriteAsJsonAsync(Meines(ansicht), cancellationToken);
            }
            catch (UebergangNichtErlaubt error)
            {
                await Konflikt(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------
    // Die Seite des Unternehmens
    // -----------------------------------------------------------------------

    private static void MapFirmengespraeche(IEndpointRouteBuilder berater)
    {
        berater.MapPost("/conversations", async (
            OpenConversationV1? koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            if (koerper is null || koerper.SubjectId == Guid.Empty)
            {
                await Unbrauchbar(context, "subject_id is required");
                return;
            }

            try
            {
                var (ansicht, neu) = await mediator.Send(
                    new OpenConversationCommand(
                        new SubjectId(koerper.SubjectId), company, koerper.Note),
                    cancellationToken);

                context.Response.StatusCode =
                    neu ? StatusCodes.Status201Created : StatusCodes.Status200OK;

                await context.Response.WriteAsJsonAsync(Ihres(ansicht), cancellationToken);
            }
            catch (Eingabefehler error)
            {
                await Unbrauchbar(context, error.Message);
            }
        });

        berater.MapGet("/conversations", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var ihre = await mediator.Send(
                new CompanyConversationsQuery(company), cancellationToken);

            // None Gesamtzahl (ADR-0026): sie verriete ueber die Differenz zur
            // Laenge, wie viele Gespraeche gerade NICHT auf Stufe 1 stehen.
            await context.Response.WriteAsJsonAsync(
                new ConversationListV1([.. ihre.Select(Ihres)]), cancellationToken);
        });

        berater.MapGet("/conversations/{id:guid}", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            var ansicht = await mediator.Send(
                new CompanyConversationQuery(id, company), cancellationToken);

            await context.Response.WriteAsJsonAsync(Ihres(ansicht), cancellationToken);
        });

        // Die Einigung wird ein Case. Der Dreieckskonsens steht in
        // transfer-service und wird hier nicht nachgebaut — diese Route ruft
        // seine Tuer und reicht die Antwort ehrlich weiter.
        berater.MapPost("/conversations/{id:guid}/hand-over", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                var caseItem = await mediator.Send(
                    new UebergebenBefehl(id, company), cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, object?> { ["transfer_id"] = caseItem },
                    cancellationToken);
            }
            catch (UebergangNichtErlaubt error)
            {
                await Konflikt(context, error.Message);
            }
            catch (HandoverRefused error)
            {
                // Eine Ablehnung von dort ist eine Aussage ueber die Bedingungen
                // dieses Vorgangs — nicht ueber diesen Dienst, und keine 500.
                await Konflikt(context, error.Message);
            }
        });

        berater.MapPost("/conversations/{id:guid}/end", async (
            Guid id,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (Firma(akteur) is not { } company)
            {
                await OhneFirma(akteur, context);
                return;
            }

            try
            {
                var ansicht = await mediator.Send(
                    new BeendenBefehl(id, null, company), cancellationToken);

                // Ohne Mandate und ohne Klarnamen: ein beendetes Conversation ist
                // kein Anlass, noch einmal etwas ueber einen Menschen zu holen.
                await context.Response.WriteAsJsonAsync(Ihres(ansicht), cancellationToken);
            }
            catch (UebergangNichtErlaubt error)
            {
                await Konflikt(context, error.Message);
            }
        });
    }

    // -----------------------------------------------------------------------

    private static async Task Stufenzug(
        Guid id,
        StufeV1? koerper,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken,
        Func<Guid, SubjectId, Stufe, IRequest<Stufe>> baue,
        Stufe defaultStage)
    {
        if (akteur.Current is not { } handelnder)
        {
            await NichtAngemeldet(context);
            return;
        }

        if (koerper?.Stage is { } genannt && StageCapabilities.Lies(genannt) is null)
        {
            await Unbrauchbar(context, "a stage is 1, 2 or 3");
            return;
        }

        var stage = StageCapabilities.Lies(koerper?.Stage) ?? default;

        var steht = await mediator.Send(baue(id, handelnder.Subject, stage), cancellationToken);

        await context.Response.WriteAsJsonAsync(
            new StufeV1(StageCapabilities.Zahl(steht)), cancellationToken);
    }

    /// <summary>Das Unternehmen aus dem Token — nie aus der ResumeRequest.</summary>
    /// <remarks>
    /// Der Mandant kommt aus dem geprüften Token (ADR-0017/0018). Ihn aus einem
    /// Header oder einem Feld zu nehmen hiesse, den Aufrufer entscheiden zu
    /// lassen, in wessen Namen er spricht.
    /// </remarks>
    private static TenantId? Firma(ICurrentPrincipal akteur) =>
        akteur.Current?.Acting is Capacity.ForCompany company ? company.Tenant : null;

    private static Task OhneFirma(ICurrentPrincipal akteur, HttpContext context) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            // 403 ist eine Aussage ueber den Aufrufer und verraet nichts ueber
            // den Menschen, nach dem er fragt.
            : ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "no active company");

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");

    private static Task Unbrauchbar(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status422UnprocessableEntity, "Request failed", detail);

    private static Task Konflikt(HttpContext context, string detail) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status409Conflict, "Request failed", detail);

    /// <summary>
    /// Was ein Filter zurückgibt, der die Antwort selbst geschrieben hat.
    /// </summary>
    /// <remarks>
    /// <strong>Nicht <c>null</c>.</strong> Ein Filter, der <c>null</c>
    /// zurückgibt, lässt das Rahmenwerk noch einmal schreiben — JSON-<c>null</c>
    /// samt Kopfzeilen, und die stehen zu diesem Zeitpunkt schon. Bei einer
    /// ResumeRequest mit Rumpf reisst dann die Connection.
    /// </remarks>
    private static readonly IResult Geschrieben = Results.Empty;

    private static async ValueTask<object?> Abfangen(
        EndpointFilterInvocationContext call, EndpointFilterDelegate weiter)
    {
        try
        {
            return await weiter(call);
        }
        catch (NoConversation error)
        {
            // DREI Lagen, EINE Antwort: gibt es nicht, steht auf keiner Stufe
            // mehr, oder das Unternehmen ist ausgeschlossen. Ein eigener Code
            // fuer die zweite oder dritte waere der „gesperrt"-Hinweis, der
            // verraet, dass es etwas gibt.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status404NotFound,
                "Request failed", error.Message);

            return Geschrieben;
        }
        catch (NichtDeins error)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status404NotFound,
                "Request failed", error.Message);

            return Geschrieben;
        }
        catch (ConsentRefused error)
        {
            // Der Ledger hat geantwortet, und zwar mit Nein. Das ist eine
            // Aussage ueber den AUFRUFER — er wollte fuer einen fremden
            // Menschen freigeben — und verraet ueber diesen nichts.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status403Forbidden,
                "Request failed", error.Message);

            return Geschrieben;
        }
        catch (ConsentGateSilent error)
        {
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", error.Message);

            return Geschrieben;
        }
        catch (LookupSilent error)
        {
            // Ausdruecklich kein leeres Ergebnis und kein 404: beides waere eine
            // Aussage ueber einen Menschen, die aus unserem Ausfall stammt.
            await ProblemDetailsMiddleware.Write(
                call.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "Request failed", error.Message);

            return Geschrieben;
        }
    }

    private static MandateV1 Antwort(Mandate mandate) =>
        new(
            mandate.Eintrittstermin,
            mandate.GehaltMin,
            mandate.GehaltMax,
            mandate.PensumProzent,
            mandate.AusgeschlosseneUnternehmen,
            mandate.GeaendertAm);

    private static MyConversationV1 Meines(ConversationView ansicht) =>
        new(
            ansicht.Conversation.Id,
            ansicht.Conversation.Firma.Value,
            ConversationStates.Wort(ansicht.Conversation.State),
            StageCapabilities.Zahl(ansicht.Stufe),
            ansicht.Conversation.Anlass,
            ansicht.Conversation.EroeffnetAm,
            ansicht.Conversation.GeaendertAm);

    /// <summary>
    /// Was das Unternehmen sieht — und was nicht einmal als Feld erscheint.
    /// </summary>
    /// <remarks>
    /// Die Auswahl steht in <see cref="Gespraechsansicht.Baue"/> und nicht hier:
    /// dieselbe Entscheidung an zwei Jobs wäre eine, die die zweite
    /// irgendwann vergisst. Diese Methode schreibt nur ab — was <c>null</c> ist,
    /// fehlt auf dem Draht.
    /// </remarks>
    private static ConversationV1 Ihres(ConversationView ansicht) =>
        new(
            ansicht.Conversation.Id,
            ansicht.Conversation.Wer.Value,
            ConversationStates.Wort(ansicht.Conversation.State),
            StageCapabilities.Zahl(ansicht.Stufe),
            ansicht.Conversation.Anlass,
            ansicht.Conversation.EroeffnetAm,
            ansicht.Conversation.GeaendertAm)
        {
            EntryMonth = ansicht.Eintrittstermin,
            WorkloadPercent = ansicht.PensumProzent,
            SalaryMin = ansicht.GehaltMin,
            SalaryMax = ansicht.GehaltMax,
            Name = ansicht.Name,
            Email = ansicht.Email
        };
}
