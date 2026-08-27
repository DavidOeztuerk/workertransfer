using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Vorgaenge;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.Transfer.Contracts;

namespace WorkerTransfer.Transfer.Api;

/// <summary><c>/transfers/*</c> — drei Ja, jederzeit ein Nein.</summary>
/// <remarks>
/// Getrennte Endpunkte statt eines <c>PATCH status</c>: jeder Übergang gehört
/// einer Seite, und ein gemeinsamer müsste bei jedem Aufruf herausfinden, wer
/// gerade was darf. Getrennt steht es in der Adresse.
/// </remarks>
public static class VorgangsEndpoints
{
    /// <summary>Bildet die neun Routen der Vorgänge ab.</summary>
    public static IEndpointRouteBuilder MapVorgangsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var vorgaenge = Antworten.Abgesichert(app.MapGroup("/transfers"));

        // Ein Unternehmen zeigt Interesse. Voraussetzung: der Marktstatus ist
        // diesem Unternehmen freigegeben UND die Person ist ansprechbar.
        // `unavailable` heißt nein, auch mit Freigabe — die Freigabe erlaubt zu
        // sehen, nicht zu stören.
        vorgaenge.MapPost("/", async (
            InteresseZeigenV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            // Ohne `subjectId` gibt es niemanden, an dem Interesse bestuende.
            // Der leere Guid lief frueher bis in `new SubjectId(...)` und kam
            // als 500 zurueck (D2) — eine kaputte Anfrage, keine kaputte
            // Anwendung.
            if (body.SubjectId == Guid.Empty)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", "subjectId is required");
                return;
            }

            var ergebnis = await mediator.Send(
                new InteresseZeigenBefehl(new SubjectId(body.SubjectId), firma, body.Message),
                cancellationToken);

            await Antworten.Schreibe(
                context, ergebnis, cancellationToken, StatusCodes.Status201Created);
        });

        vorgaenge.MapGet("/me", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (akteur.Current is not { } handelnder)
            {
                await Antworten.NichtAngemeldet(context);
                return;
            }

            var meine = await mediator.Send(
                new MeineVorgaengeAbfrage(handelnder.Subject), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antworten.Zu), cancellationToken);
        });

        vorgaenge.MapGet("/", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var ihre = await mediator.Send(new FirmenvorgaengeAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                ihre.Select(Antworten.Zu), cancellationToken);
        });

        vorgaenge.MapPost("/{id:guid}/accept-talk", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.GespraechAnnehmen, mediator, akteur, context,
                cancellationToken));

        vorgaenge.MapPost("/{id:guid}/accept-offer", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.AngebotAnnehmen, mediator, akteur, context,
                cancellationToken));

        // Die Person bestätigt, dass ihr Arbeitgeber sie gehen lässt. Die
        // Plattform prüft das nicht und kann es nicht — sie kennt weder den
        // Arbeitgeber noch den Vertrag. Was der Schritt leistet, ist, die Frage
        // zu stellen und die Antwort festzuhalten.
        vorgaenge.MapPost("/{id:guid}/confirm-release", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.FreigabeBestaetigen, mediator, akteur, context,
                cancellationToken));

        // Immer möglich, aus jedem laufenden Zustand. Ein Verfahren, aus dem
        // man nicht aussteigen kann, ist kein Verfahren, sondern eine Falle.
        vorgaenge.MapPost("/{id:guid}/decline", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Person(id, Personenzug.Absagen, mediator, akteur, context, cancellationToken));

        vorgaenge.MapPost("/{id:guid}/offer", async (
            Guid id,
            AngebotV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Antworten.Firma(context, akteur) is not { } firma)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            var ergebnis = await mediator.Send(
                new AngebotMachenBefehl(id, firma, body.Note, body.StartOn, body.FeeCents),
                cancellationToken);

            await Antworten.Schreibe(context, ergebnis, cancellationToken);
        });

        vorgaenge.MapPost("/{id:guid}/complete", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Firma(id, Firmenzug.Abschliessen, mediator, akteur, context, cancellationToken));

        vorgaenge.MapPost("/{id:guid}/withdraw", (
                Guid id, IMediator mediator, ICurrentPrincipal akteur,
                HttpContext context, CancellationToken cancellationToken) =>
            Firma(id, Firmenzug.Zurueckziehen, mediator, akteur, context, cancellationToken));

        return app;
    }

    private static async Task Person(
        Guid id,
        Personenzug zug,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (akteur.Current is not { } handelnder)
        {
            await Antworten.NichtAngemeldet(context);
            return;
        }

        var ergebnis = await mediator.Send(
            new PersonenzugBefehl(id, handelnder.Subject, zug), cancellationToken);

        await Antworten.Schreibe(context, ergebnis, cancellationToken);
    }

    private static async Task Firma(
        Guid id,
        Firmenzug zug,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (await Antworten.Firma(context, akteur) is not { } firma)
        {
            return;
        }

        var ergebnis = await mediator.Send(new FirmenzugBefehl(id, firma, zug), cancellationToken);

        await Antworten.Schreibe(context, ergebnis, cancellationToken);
    }
}
