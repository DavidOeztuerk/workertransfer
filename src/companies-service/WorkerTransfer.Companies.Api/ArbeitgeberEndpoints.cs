using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Companies.Api.Berechtigung;
using WorkerTransfer.Companies.Application.Profile;
using WorkerTransfer.Companies.Contracts;
using WorkerTransfer.Companies.Domain.EmployerProfiles;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Companies.Api;

/// <summary>Die vier Routen des Arbeitgeberprofils.</summary>
/// <remarks>
/// Drei davon brauchen keine Anmeldung, und das ist der Zweck: ein
/// EmployerProfile ist die Selbstdarstellung eines Unternehmens: es hinter
/// eine Anmeldung zu legen wäre das Gegenteil — genau wie bei den Jobs.
/// </remarks>
public static class EmployerEndpoints
{
    /// <summary>Bildet alle Routen dieses Dienstes ab.</summary>
    public static IEndpointRouteBuilder MapArbeitgeberEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var companies = app.MapGroup("/companies");

        companies.MapPut("/me/profile", async (
            WriteEmployerProfileV1 body,
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } company)
            {
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            try
            {
                var profile = await mediator.Send(
                    new SaveProfileCommand(
                        company, body.DisplayName, body.About, body.Website,
                        body.Locations, body.Benefits,
                        body.Line1, body.PostalCode, body.City, body.Country, body.Phone),
                    cancellationToken);

                await context.Response.WriteAsJsonAsync(Antwort(profile), cancellationToken);
            }
            catch (ProfileError error)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status422UnprocessableEntity,
                    "Request failed", error.Message);
            }
        })
        // Das Schaufenster steht oeffentlich und spricht fuer alle Mitglieder —
        // es zu schreiben verlangt deshalb einen `admin`. Aufgeloest aus der
        // Mitgliedschaftstabelle von identity-service, nie aus dem Token.
        .RequireAuthorization(
            AdminPermissionExtensions.Richtlinie(ShopWindowPermissions.Schreiben));

        // `null` statt 404: „noch keins" ist ein Zustand, kein Fehler. Die
        // Oberfläche zeigt darauf ein leeres Formular.
        companies.MapGet("/me/profile", async (
            IMediator mediator,
            ICurrentPrincipal akteur,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (await Firma(context, akteur) is not { } company)
            {
                return;
            }

            var profile = await mediator.Send(new MyProfileQuery(company), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                profile is null ? null : Antwort(profile), cancellationToken);
        });

        // Die Reihenfolge zu den beiden Routen darunter ist hier gleichgültig —
        // gemessen, nicht angenommen: `{tenantId:guid}` trägt eine Einschränkung,
        // an der `by-slug` nie vorbeikommt. Der Python-Dienst musste diese Route
        // nach vorn ziehen, weil FastAPI nach Deklarationsreihenfolge trifft;
        // diese Begründung mitzukopieren hieße, einen Satz stehen zu lassen, der
        // hier falsch ist.
        companies.MapGet("/by-slug/{slug}", async (
            string slug,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profile = await mediator.Send(
                new ProfileBySlugQuery(slug), cancellationToken);

            await Beantworte(context, profile, fehltIst404: true, cancellationToken);
        });

        // Öffentlich, ohne Anmeldung. 404, solange nichts angelegt wurde — dann
        // bleibt eine Job anonym. Ein Profile zu erzwingen, bevor jemand
        // ausschreiben darf, wäre eine Kopplung zwischen zwei Diensten für eine
        // Regel, die niemand verlangt hat.
        companies.MapGet("/{tenantId:guid}/profile", async (
            Guid tenantId,
            IMediator mediator,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var profile = await mediator.Send(
                new PublicProfileQuery(new TenantId(tenantId)), cancellationToken);

            await Beantworte(context, profile, fehltIst404: false, cancellationToken);
        });

        return app;
    }

    /// <summary>Die Firma des Aufrufers, oder eine schon geschriebene Absage.</summary>
    private static async Task<TenantId?> Firma(HttpContext context, ICurrentPrincipal akteur)
    {
        if (akteur.Current is not { } handelnder)
        {
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated");

            return null;
        }

        if (handelnder.Acting is not Capacity.ForCompany company)
        {
            // Eine Aussage über den Aufrufer, nicht über ein fremdes
            // Unternehmen: eine Privatperson weiß, dass sie keines ist.
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "editing a company profile requires an active company");

            return null;
        }

        return company.Tenant;
    }

    private static async Task Beantworte(
        HttpContext context,
        EmployerProfile? profile,
        bool fehltIst404,
        CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            // Kürzel: die Adresse selbst behauptet eine Firma. Tenant-Id:
            // die Job ist anonym, 200 null — sonst loggt /jobs jede Karte.
            if (fehltIst404)
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status404NotFound,
                    "Request failed", "No such company profile");
                return;
            }

            await context.Response.WriteAsJsonAsync(profile, cancellationToken);
            return;
        }

        await context.Response.WriteAsJsonAsync(Antwort(profile), cancellationToken);
    }

    private static EmployerProfileV1 Antwort(EmployerProfile profile) =>
        new(profile.Firma.Value,
            profile.Slug,
            profile.Anzeigename,
            profile.UeberUns,
            profile.Netzseite,
            profile.Orte,
            profile.Leistungen,
            profile.Zeile1,
            profile.Postleitzahl,
            profile.Ort,
            profile.Land,
            profile.Telefon,
            profile.GeaendertAm);
}
