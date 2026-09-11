using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Loeschung;
using WorkerTransfer.Advisor.Infrastructure.Loeschung;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Advisor.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Löschkaskade.</summary>
/// <remarks>
/// Dieser Dienst ist Löschempfänger, seit er seine erste Tabelle hat
/// (ADR-0027 §4, ADR-0037): <c>mandates</c> trägt die Person als Schlüssel,
/// <c>conversations</c> eine <c>subject_id</c>, der Postausgang eine
/// <c>user_id</c>. <c>"advisor"</c> steht deshalb in
/// <c>Loeschempfaenger.Fremde</c> und in <c>LoeschempfaengerTests.Dienste</c>.
/// </remarks>
public static class LoeschEndpoints
{
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bindet den einen Endpunkt ein, den die Kaskade ruft.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpunkt(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            LoeschungV1 koerper,
            IMediator mediator,
            IOptions<Loescheinstellungen> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfLoeschen(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            ArgumentNullException.ThrowIfNull(koerper);

            var geblieben = await mediator.Send(
                new PersonLoeschenBefehl(new SubjectId(koerper.UserId)), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new LoeschungsquittungV1(geblieben), cancellationToken);
        });

        return app;
    }

    private static bool DarfLoeschen(HttpContext context, Loescheinstellungen einstellungen)
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
