using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Application.Benachrichtigung;
using WorkerTransfer.Identity.Infrastructure.Post;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api;

/// <summary><c>POST /internal/notify</c> — der einzige Weg zu einer Adresse.</summary>
/// <remarks>
/// Der Python-Dienst hielt die Benachrichtigungen hier, weil „ein
/// <c>notifications-service</c> die E-Mail-Adresse bräuchte". Der Einwand
/// stimmt; beantwortet wird er nicht, indem man ihn ignoriert, sondern indem
/// die <em>andere</em> Hälfte umzieht. Bei notification-service liegt,
/// <strong>ob</strong> etwas hinausgeht — die vier Schalter und die Drossel.
/// Hier liegt, <strong>an wen</strong>. Über die Grenze geht nur eine
/// <c>userId</c>, die überall sonst auch schon geht.
/// <para>
/// Deshalb trägt der Rumpf auch keine Art und keinen Text: der Satz entsteht
/// hier, immer derselbe, und der Aufrufer kann nichts beisteuern.
/// </para>
/// </remarks>
public static class MeldeEndpoints
{
    /// <summary>Der Kopf, den notification-service mitschickt.</summary>
    private const string Geheimniskopf = "X-Notify-Secret";

    /// <summary>Was hereinkommt: eine Kennung, sonst nichts.</summary>
    private sealed record MeldungV1(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] Guid UserId);

    /// <summary>Bildet den einen Endpunkt ab.</summary>
    public static IEndpointRouteBuilder MapMeldeEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/internal/notify", async (
            MeldungV1 body,
            IMediator mediator,
            IOptions<Meldeeinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfMelden(context, einstellungen.Value))
            {
                // 404 statt 401: ein 401 bestätigt, dass es den Endpunkt gibt.
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status404NotFound, "Request failed", "Not Found");
                return;
            }

            ArgumentNullException.ThrowIfNull(body);

            // Das Ergebnis wird verworfen. Ob es die Person gibt und ob ihr
            // Konto bestätigt ist, geht den Aufrufer nichts an — sonst wäre der
            // Endpunkt ein Orakel über die Mitgliedschaft auf dieser Plattform.
            await mediator.Send(
                new NeuigkeitMeldenBefehl(new SubjectId(body.UserId)), cancellationToken);

            context.Response.StatusCode = StatusCodes.Status202Accepted;
        });

        return app;
    }

    /// <summary>Ob diese Anfrage das Geheimnis trägt.</summary>
    /// <remarks>
    /// Ein leeres eingestelltes Geheimnis schließt den Endpunkt. In fester Zeit
    /// verglichen, damit die Dauer der Antwort nichts darüber sagt, wie viel
    /// von einem Rateversuch stimmte.
    /// </remarks>
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
