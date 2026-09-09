using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Application.Profile;
using WorkerTransfer.Companies.Contracts;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Companies.Api;

/// <summary>Die vier Routen des Arbeitgeberprofils.</summary>
/// <remarks>
/// Drei davon brauchen keine Anmeldung, und das ist der Zweck: ein
/// Arbeitgeberprofil ist die Selbstdarstellung eines Unternehmens: es hinter
/// eine Anmeldung zu legen wäre das Gegenteil — genau wie bei den Stellen.
/// </remarks>
public static class ArbeitgeberEndpoints
{
    /// <summary>Bildet alle Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapArbeitgeberEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var firmen = app.MapGroup("/companies");

        firmen.MapPut("/me/profile", async (
            ArbeitgeberprofilSchreibenV1 body,
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

            try
            {
                var profil = await mediator.Send(
                    new ProfilSichernBefehl(
                        firma, body.DisplayName, body.About, body.Website,
                        body.Locations, body.Benefits,
                        body.Line1, body.PostalCode, body.City, body.Country, body.Phone),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(profil), cancellationToken);
            }
            catch (Profilfehler fehler)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", fehler.Message);
            }
        });

        // `null` statt 404: „noch keins" ist ein Zustand, kein Fehler. Die
        // Oberfläche zeigt darauf ein leeres Formular.
        firmen.MapGet("/me/profile", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } firma)
            {
                return;
            }

            var profil = await mediator.Send(new MeinProfilAbfrage(firma), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                profil is null ? null : Antwort(profil), cancellationToken);
        });

        // Die Reihenfolge zu den beiden Routen darunter ist hier gleichgültig —
        // gemessen, nicht angenommen: `{tenantId:guid}` trägt eine Einschränkung,
        // an der `by-slug` nie vorbeikommt. Der Python-Dienst musste diese Route
        // nach vorn ziehen, weil FastAPI nach Deklarationsreihenfolge trifft;
        // diese Begründung mitzukopieren hieße, einen Satz stehen zu lassen, der
        // hier falsch ist.
        firmen.MapGet("/by-slug/{kuerzel}", async (
            string kuerzel,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profil = await mediator.Send(
                new ProfilNachKuerzelAbfrage(kuerzel), cancellationToken);

            await Beantworte(context, profil, fehltIst404: true, cancellationToken);
        });

        // Öffentlich, ohne Anmeldung. 404, solange nichts angelegt wurde — dann
        // bleibt eine Stelle anonym. Ein Profil zu erzwingen, bevor jemand
        // ausschreiben darf, wäre eine Kopplung zwischen zwei Diensten für eine
        // Regel, die niemand verlangt hat.
        firmen.MapGet("/{tenantId:guid}/profile", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profil = await mediator.Send(
                new OeffentlichesProfilAbfrage(new TenantId(tenantId)), cancellationToken);

            await Beantworte(context, profil, fehltIst404: false, cancellationToken);
        });

        return app;
    }

    /// <summary>Die Firma des Aufrufers, oder eine schon geschriebene Absage.</summary>
    private static async Task<TenantId?> Firma(HttpContext context, ICurrentPrincipal akteur)
    {
        if (akteur.Current is not { } handelnder)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated");

            return null;
        }

        if (handelnder.Acting is not Capacity.ForCompany firma)
        {
            // Eine Aussage über den Aufrufer, nicht über ein fremdes
            // Unternehmen: eine Privatperson weiß, dass sie keines ist.
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "editing a company profile requires an active company");

            return null;
        }

        return firma.Tenant;
    }

    private static async Task Beantworte(
        HttpContext context,
        Arbeitgeberprofil? profil,
        bool fehltIst404,
        CancellationToken cancellationToken)
    {
        if (profil is null)
        {
            // Kürzel: die Adresse selbst behauptet eine Firma. Tenant-Id:
            // die Stelle ist anonym, 200 null — sonst loggt /jobs jede Karte.
            if (fehltIst404)
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such company profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(profil, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(Antwort(profil), cancellationToken);
    }

    private static ArbeitgeberprofilV1 Antwort(Arbeitgeberprofil profil) =>
        new(profil.Firma.Value,
            profil.Kuerzel,
            profil.Anzeigename,
            profil.UeberUns,
            profil.Netzseite,
            profil.Orte,
            profil.Leistungen,
            profil.Zeile1,
            profil.Postleitzahl,
            profil.Ort,
            profil.Land,
            profil.Telefon,
            profil.GeaendertAm);
}
