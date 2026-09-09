using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Identity;
using WorkerTransfer.Identity.Application.Konto;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary>Interne Dienst-zu-Dienst-Endpunkte — hinter dem gemeinsamen Geheimnis.</summary>
/// <remarks>
/// Diese Endpunkte haben KEINE Gateway-Route. Sie liegen unter `/internal/`
/// und verhalten sich wie `/internal/notify` und `/internal/notifications`:
/// ohne das Geheimnis antworten sie mit 404 (nicht 401), damit sie sich nicht
/// über eine Methode verraten, die niemand benutzt.
/// </remarks>
public static class InterneEndpoints
{
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Bildet die internen Routen ab.</summary>
    public static IEndpointRouteBuilder MapInterneEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/internal/companies/{tenantId:guid}/members", async (
            Guid tenantId,
            IMediator mediator,
            IOptions<Meldeeinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var mitglieder = await mediator.Send(
                new InterneMitgliederAbfrage(new TenantId(tenantId)),
                cancellationToken);

            var antwort = new UnternehmensmitgliederAntwortV1(
                [.. mitglieder.Select(m => new UnternehmensmitgliedV1(m.Subject.Value))]);

            await context.Response.WriteAsJsonAsync(antwort, cancellationToken);
        });

        app.MapGet("/internal/account/{subjectId:guid}/ai", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<Meldeeinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            var zugang = await mediator.Send(
                new InterneKiZugangAbfrage(new SubjectId(subjectId)),
                cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new KiZugangV1(zugang.Anbieter, zugang.Adresse, zugang.Modell, zugang.Schluessel),
                cancellationToken);
        });

        return app;
    }

    private static bool DarfMelden(HttpContext context, Meldeeinstellungen einstellungen)
    {
        if (string.IsNullOrEmpty(einstellungen.Geheimnis)
            || !context.Request.Headers.TryGetValue(Geheimniskopf, out var vorgelegt))
        {
            return false;
        }

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(vorgelegt.ToString()),
            System.Text.Encoding.UTF8.GetBytes(einstellungen.Geheimnis));
    }
}
