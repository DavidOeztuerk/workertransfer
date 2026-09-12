using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Jobs.Application.Rueckzug;
using WorkerTransfer.Jobs.Infrastructure.Rueckzug;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Api;

/// <summary><c>POST /companies/withdrawal</c>.</summary>
/// <remarks>
/// <b>Kein Löschendpunkt.</b> Dieser Dienst hält nichts über eine natürliche
/// Person und ist deshalb kein Empfänger der Kaskade (ADR-0027 §2). Was hier
/// ankommt, ist die Absicht aus §7: das Unternehmen hat seine letzte
/// Administratorin verloren, und seine Anzeigen werden zurückgezogen.
/// <para>
/// Sie zählt ausdrücklich <em>nicht</em> in den Vollständigkeitsnachweis der
/// Löschung — sonst hielte ein stiller jobs-service die Löschung eines
/// Menschen offen.
/// </para>
/// </remarks>
public static class RueckzugsEndpoints
{
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bindet den einen Endpunkt ein, den der Ursprung ruft.</summary>
    public static IEndpointRouteBuilder MapRueckzugsEndpunkt(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/companies/withdrawal", async (
            UnternehmensrueckzugV1 koerper,
            IMediator mediator,
            IOptions<Rueckzugseinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfZurueckziehen(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            var zurueckgezogen = await mediator.Send(
                new AnzeigenZurueckziehenBefehl(new TenantId(koerper.TenantId)),
                cancellationToken);

            // Zurückgezogen, nicht gelöscht: ein Unternehmen ist keine
            // natürliche Person, und seine Anzeigen gehören ihm auch dann noch.
            await context.Response.WriteAsJsonAsync(
                new Dictionary<string, int> { ["withdrawn"] = zurueckgezogen },
                cancellationToken);
        });

        return app;
    }

    private static bool DarfZurueckziehen(
        HttpContext context, Rueckzugseinstellungen einstellungen)
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
