using Noelia.Core.Identity;
using MediatR;
using Microsoft.Extensions.Options;
using WorkerTransfer.Assessment.Application.Erasure;
using WorkerTransfer.Assessment.Infrastructure.Erasure;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Assessment.Api;

/// <summary><c>POST /erasure</c> — der Eingang der Löschkaskade.</summary>
/// <remarks>
/// Dieser Dienst ist Löschempfänger, seit er seine erste Tabelle hat
/// (ADR-0027 §4, ADR-0042): <c>assessments</c> trägt eine <c>subject_id</c>,
/// der Postausgang eine <c>user_id</c>. <c>"assessment"</c> steht deshalb in
/// <c>ErasureRecipient.Fremde</c> und in <c>LoeschempfaengerTests.Dienste</c>.
/// <para>
/// Und ohne Aufbewahrungsfall: eine Evaluation, die die Löschung überlebte,
/// wäre genau das Reference, das ADR-0042 ausschliesst.
/// </para>
/// </remarks>
public static class ErasureEndpoints
{
    private const string Geheimniskopf = "X-Erasure-Secret";

    /// <summary>Bindet den einen Endpunkt ein, den die Kaskade ruft.</summary>
    public static IEndpointRouteBuilder MapLoeschEndpunkt(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/erasure", async (
            ErasureV1 koerper,
            IMediator mediator,
            IOptions<ErasureSettings> einstellungen,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            if (!DarfLoeschen(context, einstellungen.Value))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status401Unauthorized,
                    "Request failed", "not authorised");
                return;
            }

            ArgumentNullException.ThrowIfNull(koerper);

            var geblieben = await mediator.Send(
                new ErasePersonCommand(new SubjectId(koerper.UserId)), cancellationToken);

            await context.Response.WriteAsJsonAsync(
                new ErasureReceiptV1(geblieben), cancellationToken);
        });

        return app;
    }

    private static bool DarfLoeschen(HttpContext context, ErasureSettings einstellungen)
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
