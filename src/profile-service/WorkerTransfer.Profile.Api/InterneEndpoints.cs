using Girder.Core.Identity;
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
/// (ADR-0030, ADR-0036 Entscheidung 1). Genau EINE Stelle entscheidet über
/// Sichtbarkeit; zwei wären zwei Wahrheiten. Wer diese Tür für etwas anderes
/// benutzt, holt sich die Prüfung dazu — oder er zeigt Profile, die niemand
/// freigegeben hat.</para>
/// </remarks>
public static class InterneEndpoints
{
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Bildet die internen Routen ab.</summary>
    public static IEndpointRouteBuilder MapInterneEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Die Suche fuer scout-service. Nur ueber GENANNTE Faehigkeiten
        // (ADR-0033): profile-service kennt keine Belege, und diese Route ist
        // ein Grund, das so zu lassen.
        app.MapGet("/internal/profiles/search", async (
            IMediator mediator,
            IOptions<Meldeeinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfFragen(context, einstellungen.Value))
            {
                await NichtGefunden(context);
                return;
            }

            var anfrage = context.Request.Query;

            var seite = await mediator.Send(
                new InterneProfilsucheAbfrage(
                    Anzahl(anfrage["limit"]),
                    anfrage["cursor"],
                    anfrage["skill"].Count > 0 ? [.. anfrage["skill"]!] : null,
                    anfrage["location"].ToString(),
                    anfrage["remote"] == "true"),
                cancellationToken);

            // Keine Gesamtzahl — auch hier nicht, wo nur ein Dienst mitliest:
            // was es nicht gibt, kann niemand versehentlich weiterreichen.
            await context.Response.WriteAsJsonAsync(
                new ProfilfundseiteV1(
                    [.. seite.Eintraege.Select(Fund)],
                    seite.Weiter?.Schreibe()),
                cancellationToken);
        });

        // Ein einzelnes Profil — fuer den Entwurf einer Ansprache.
        app.MapGet("/internal/profiles/{subjectId:guid}", async (
            Guid subjectId,
            IMediator mediator,
            IOptions<Meldeeinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfFragen(context, einstellungen.Value))
            {
                await NichtGefunden(context);
                return;
            }

            var profil = await mediator.Send(
                new MeinProfilAbfrage(new SubjectId(subjectId)), cancellationToken);

            if (profil is null)
            {
                // 404 heisst hier „es gibt keins". Ob es gezeigt werden darf,
                // hat der Aufrufer schon gefragt — hier gibt es dazu nichts zu
                // sagen.
                await NichtGefunden(context);
                return;
            }

            await context.Response.WriteAsJsonAsync(Fund(profil), cancellationToken);
        });

        return app;
    }

    private static ProfilfundV1 Fund(Profil profil) =>
        new(
            profil.Wer.Value,
            profil.Ueberschrift,
            profil.Text,
            profil.Ort,
            profil.RemoteMoeglich,
            profil.Faehigkeiten.Werte);

    /// <summary>Wie viele Zeilen eine Seite traegt.</summary>
    private static int Anzahl(string? roh) =>
        int.TryParse(roh, out var wert)
        && wert > 0
        && wert <= InterneProfilsucheHandler.Hoechstzahl
            ? wert
            : InterneProfilsucheHandler.Vorgabe;

    private static Task NichtGefunden(HttpContext context) =>
        ProblemDetailsMiddleware.Schreibe(
            context, StatusCodes.Status404NotFound, "Request failed", "Not Found");

    private static bool DarfFragen(HttpContext context, Meldeeinstellungen einstellungen)
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
