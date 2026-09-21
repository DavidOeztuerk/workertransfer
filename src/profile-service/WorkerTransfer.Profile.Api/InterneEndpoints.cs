using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Profile.Application.Profile;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.Profile.Domain.Profile;
using WorkerTransfer.Profile.Infrastructure.Intern;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Api;

/// <summary>Interne Dienst-zu-Dienst-Endpunkte — hinter dem gemeinsamen Geheimnis.</summary>
/// <remarks>
/// <para>Diese Endpunkte haben <strong>keine Gateway-Route</strong>. Sie liegen
/// unter <c>/internal/</c> und verhalten sich wie <c>/internal/notify</c> und
/// <c>/internal/notifications</c>: ohne das Geheimnis antworten sie mit 404
/// (nicht 401), damit sie sich nicht über eine Methode verraten, die niemand
/// benutzt.</para>
///
/// <para><strong>Sie prüfen den Ledger nicht, und das ist der heikelste Satz
/// dieser Datei.</strong> Der Aufrufer ist scout-service, und dort steht die
/// Freigabeprüfung — für die ganze Seite auf einmal, über <c>/check-batch</c>
/// (ADR-0030, ADR-0036 Entscheidung 1). Genau EINE Job entscheidet über
/// Sichtbarkeit; zwei wären zwei Wahrheiten. Wer diese Tür für etwas anderes
/// benutzt, holt sich die Prüfung dazu — oder er zeigt PersonProfile, die niemand
/// freigegeben hat.</para>
/// </remarks>
public static class InternalEndpoints
{
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Bildet die internen Routen ab.</summary>
    public static IEndpointRouteBuilder MapInterneEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Die Search fuer scout-service. Nur ueber GENANNTE Faehigkeiten
        // (ADR-0033): profile-service kennt keine Belege, und diese Route ist
        // ein Grund, das so zu lassen.
        app.MapGet("/internal/profiles/search", async (
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfFragen(context, einstellungen.Value))
            {
                await NichtGefunden(context);
                return;
            }

            var request = context.Request.Query;

            var page = await mediator.Send(
                new InternalProfileSearchQuery(
                    Anzahl(request["limit"]),
                    request["cursor"],
                    request["skill"].Count > 0 ? [.. request["skill"]!] : null,
                    request["location"].ToString(),
                    request["remote"] == "true"),
                cancellationToken);

            // None Gesamtzahl — auch hier nicht, wo nur ein Dienst mitliest:
            // was es nicht gibt, kann niemand versehentlich weiterreichen.
            await context.Response.WriteAsJsonAsync(
                new ProfileHitPageV1(
                    [.. page.Eintraege.Select(Fund)],
                    page.Weiter?.Write()),
                cancellationToken);
        });

        // Ein einzelnes PersonProfile — fuer den Entwurf einer Ansprache.
        app.MapGet("/internal/profiles/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<NotifySettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfFragen(context, einstellungen.Value))
            {
                await NichtGefunden(context);
                return;
            }

            var profile = await mediator.Send(
                new MyProfileQuery(new SubjectId(subjectId)), cancellationToken);

            if (profile is null)
            {
                // 404 heisst hier „es gibt keins". Ob es gezeigt werden darf,
                // hat der Aufrufer schon gefragt — hier gibt es dazu nichts zu
                // sagen.
                await NichtGefunden(context);
                return;
            }

            await context.Response.WriteAsJsonAsync(Fund(profile), cancellationToken);
        });

        return app;
    }

    private static ProfileHitV1 Fund(PersonProfile profile) =>
        new(
            profile.Wer.Value,
            profile.Ueberschrift,
            profile.Text,
            profile.Ort,
            profile.RemoteMoeglich,
            profile.Faehigkeiten.Werte,
            Pendelstufen.Wort(profile.Pendelbereitschaft),
            RelocationTerms.Wort(profile.Umzugsbereitschaft));

    /// <summary>Wie viele Zeilen eine Seite traegt.</summary>
    private static int Anzahl(string? roh) =>
        int.TryParse(roh, out var value)
        && value > 0
        && value <= InternalProfileSearchHandler.Hoechstzahl
            ? value
            : InternalProfileSearchHandler.Vorgabe;

    private static Task NichtGefunden(HttpContext context) =>
        ProblemDetailsMiddleware.Write(
            context, StatusCodes.Status404NotFound, "Request failed", "Not Found");

    private static bool DarfFragen(HttpContext context, NotifySettings einstellungen)
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
