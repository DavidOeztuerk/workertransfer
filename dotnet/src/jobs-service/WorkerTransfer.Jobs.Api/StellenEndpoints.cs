using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Jobs.Application.Entwurf;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Application.Stellen;
using WorkerTransfer.Jobs.Contracts;
using WorkerTransfer.Jobs.Domain.Stellen;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>/jobs</c> und <c>/companies/me/jobs</c>.</summary>
public static class StellenEndpoints
{
    /// <summary>Bindet die sieben Stellenrouten ein.</summary>
    public static IEndpointRouteBuilder MapStellenEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var stellen = app.MapGroup("/jobs");

        stellen.MapPost("/draft", async (
            EntwurfV1 koerper,
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

            if (handelnder.Acting is not Capacity.ForCompany)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status403Forbidden,
                    "Request failed", "no active company");
                return;
            }

            try
            {
                // Die tenant_id wird ABSICHTLICH nicht weitergereicht: der
                // Entwurfskontext hat kein Feld dafür, also kann auch nichts
                // hineinrutschen (ADR-0024).
                var text = await mediator.Send(
                    new AnzeigeEntwerfenAbfrage(
                        koerper.Title, koerper.Description, koerper.Skills ?? [],
                        koerper.Location, koerper.Wish),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(
                    new Dictionary<string, string> { ["draft"] = text }, cancellationToken);
            }
            catch (EntwurfNichtVerfuegbar fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", fehler.Message);
            }
        });

        stellen.MapPost("/", async (
            StelleSchreibenV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var stelle = await mediator.Send(
                    new StelleAnlegenBefehl(firma, Angaben(koerper)), cancellationToken);

                context.Response.StatusCode = StatusCodes.Status201Created;
                await context.Response.WriteAsJsonAsync(Antwort(stelle), cancellationToken);
            });
        });

        stellen.MapPut("/{id:guid}", async (
            Guid id,
            StelleSchreibenV1 koerper,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            await Beantworte(context, cancellationToken, async () =>
            {
                var stelle = await mediator.Send(
                    new StelleAendernBefehl(firma, id, Angaben(koerper)), cancellationToken);

                await Zeige(context, stelle, cancellationToken);
            });
        });

        stellen.MapPost("/{id:guid}/publish", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, Stellenstand.Published, mediator, akteur, context, cancellationToken));

        stellen.MapPost("/{id:guid}/close", (
            Guid id, IMediator mediator, ICurrentPrincipal akteur,
            HttpContext context, CancellationToken cancellationToken) =>
            Wechsle(id, Stellenstand.Closed, mediator, akteur, context, cancellationToken));

        // Öffentlich für jeden Angemeldeten — und ohne jede Sortierung nach
        // Passung. Wie gut jemand zu einer Stelle passt, rechnet der Browser
        // und zeigt es der Person (ADR-0022).
        stellen.MapGet("/", async (
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

            var anfrage = context.Request.Query;

            var seite = await mediator.Send(
                new StellensucheAbfrage(
                    Anzahl(anfrage["limit"]),
                    anfrage["cursor"],
                    anfrage["skill"].Count > 0 ? [.. anfrage["skill"]!] : null,
                    anfrage["location"].ToString(),
                    Grad(anfrage["remote"])),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new StellenseiteV1([.. seite.Eintraege.Select(Antwort)], seite.Weiter),
                cancellationToken);
        });

        stellen.MapGet("/{id:guid}", async (
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

            var stelle = await mediator.Send(
                new StelleAbfrage(
                    id,
                    handelnder.Acting is Capacity.ForCompany firma ? firma.Tenant : null),
                cancellationToken);

            await Zeige(context, stelle, cancellationToken);
        });

        app.MapGet("/companies/me/jobs", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!Firma(akteur, out var firma))
            {
                await KeineFirma(context, akteur);
                return;
            }

            var meine = await mediator.Send(new MeineStellenAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                meine.Select(Antwort), cancellationToken);
        });

        return app;
    }

    private static async Task Wechsle(
        Guid id,
        Stellenstand ziel,
        IMediator mediator,
        ICurrentPrincipal akteur,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (!Firma(akteur, out var firma))
        {
            await KeineFirma(context, akteur);
            return;
        }

        await Beantworte(context, cancellationToken, async () =>
        {
            var stelle = await mediator.Send(
                new StandAendernBefehl(firma, id, ziel), cancellationToken);

            await Zeige(context, stelle, cancellationToken);
        });
    }

    /// <summary>
    /// Zeigt sie, oder antwortet gleichlautend für „gibt es nicht" und „gehört
    /// einem anderen Unternehmen".
    /// </summary>
    /// <remarks>
    /// Ein Unterschied verriete, welche Anzeigen es anderswo gibt — und ein
    /// Entwurf ist gerade das, was ein Unternehmen noch nicht zeigen will.
    /// </remarks>
    private static Task Zeige(HttpContext context, Stelle? stelle, CancellationToken abbruch) =>
        stelle is null
            ? ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status404NotFound, "Request failed", "no such job")
            : context.Response.WriteAsJsonAsync(Antwort(stelle), abbruch);

    private static async Task Beantworte(
        HttpContext context, CancellationToken abbruch, Func<Task> arbeit)
    {
        try
        {
            await arbeit();
        }
        catch (UebergangNichtErlaubt fehler)
        {
            // 409, nicht 422: der Aufrufer darf das grundsätzlich, nur diese
            // Anzeige ist gerade nicht in dem Zustand.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status409Conflict, "Request failed", fehler.Message);
        }
        catch (Eingabefehler fehler)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status422UnprocessableEntity,
                "Request failed", fehler.Message);
        }

        _ = abbruch;
    }

    private static bool Firma(ICurrentPrincipal akteur, out TenantId firma)
    {
        if (akteur.Current?.Acting is Capacity.ForCompany fuer)
        {
            firma = fuer.Tenant;
            return true;
        }

        firma = TenantId.None;
        return false;
    }

    private static Task KeineFirma(HttpContext context, ICurrentPrincipal akteur) =>
        akteur.Current is null
            ? NichtAngemeldet(context)
            : ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden, "Request failed", "no active company");

    private static Stellenangaben Angaben(StelleSchreibenV1 koerper) => new(
        koerper.Title, koerper.Description, koerper.Location,
        Grad(koerper.RemoteMode) ?? Remotegrad.None,
        Anstellung(koerper.EmploymentType),
        koerper.Skills ?? []);

    private static int Anzahl(string? roh) =>
        int.TryParse(roh, out var wert) && wert is > 0 and <= 50 ? wert : 20;

    private static Remotegrad? Grad(string? roh) => roh?.Trim().ToLowerInvariant() switch
    {
        "none" => Remotegrad.None,
        "hybrid" => Remotegrad.Hybrid,
        "full" => Remotegrad.Full,
        _ => null
    };

    private static Anstellungsart Anstellung(string? roh) => roh?.Trim().ToLowerInvariant() switch
    {
        "part_time" => Anstellungsart.PartTime,
        "contract" => Anstellungsart.Contract,
        "internship" => Anstellungsart.Internship,
        _ => Anstellungsart.FullTime
    };

    private static StelleV1 Antwort(Stelle stelle) => new(
        stelle.Id,
        stelle.Firma.Value,
        stelle.Titel,
        stelle.Beschreibung,
        stelle.Ort,
        stelle.Remote.ToString().ToLowerInvariant(),
        stelle.Art switch
        {
            Anstellungsart.PartTime => "part_time",
            Anstellungsart.Contract => "contract",
            Anstellungsart.Internship => "internship",
            _ => "full_time"
        },
        stelle.Faehigkeiten.Werte,
        stelle.Stand.ToString().ToLowerInvariant(),
        stelle.VeroeffentlichtAm,
        stelle.GeaendertAm);

    private static Task NichtAngemeldet(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status401Unauthorized, "Request failed", "not authenticated");
}
