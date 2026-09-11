using Girder.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Assessment.Application.Loeschung;
using WorkerTransfer.Assessment.Infrastructure.Loeschung;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Löschkaskade.</summary>
/// <remarks>
/// Dieser Dienst ist Löschempfänger, seit er seine erste Tabelle hat
/// (ADR-0027 §4, ADR-0042): <c>assessments</c> trägt eine <c>subject_id</c>,
/// der Postausgang eine <c>user_id</c>. <c>"assessment"</c> steht deshalb in
/// <c>Loeschempfaenger.Fremde</c> und in <c>LoeschempfaengerTests.Dienste</c>.
/// <para>
/// Und ohne Aufbewahrungsfall: eine Bewertung, die die Löschung überlebte,
/// wäre genau das Zeugnis, das ADR-0042 ausschliesst.
/// </para>
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
