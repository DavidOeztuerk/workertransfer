using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Gibt einer Ablehnung durch eine Richtlinie dieselbe Gestalt wie jedem
/// anderen Fehler.
/// </summary>
/// <remarks>
/// <para>Ohne das fällt eine gescheiterte Autorisierung als <strong>nackter
/// 401/403 mit leerem Rumpf</strong> heraus: sie wirft nicht, sie schließt die
/// Antwort kurz — <c>ProblemDetailsMiddleware</c> sieht sie also nie. Die
/// Oberfläche bekäme dann für dieselbe Sache zwei Gestalten, je nachdem, ob
/// eine Handprüfung oder eine Richtlinie abgelehnt hat, und der Bericht eines
/// Menschen hätte keine Korrelationskennung.</para>
///
/// <para><strong>Hier und nicht in identity-service.</strong> Sie stand dort,
/// solange identity der einzige Dienst mit Richtlinien war. Seit die
/// Firmenrechte in vier Diensten hängen, ist sie genau das, was
/// <c>ServiceDefaults</c> trägt: eine Gestalt über alle Dienste, damit ein
/// Aufrufer nicht wissen muss, wer geantwortet hat, um den Fehler zu lesen.</para>
///
/// <para>Die Meldung sagt nur, dass es nicht erlaubt war. <em>Welche</em>
/// Richtlinie fehlte, steht bewusst nicht darin: das wäre eine Landkarte der
/// Rechte für jeden, der sie abfragt.</para>
/// </remarks>
public sealed class Ablehnungsgestalt : IAuthorizationMiddlewareResultHandler
{
    /// <summary>
    /// Die Notiz, mit der ein Handler sagt: es lag nicht am Recht, sondern
    /// daran, dass niemand geantwortet hat.
    /// </summary>
    /// <remarks>
    /// Ein Autorisierungshandler kann nur „ja" sagen — ein Schweigen der
    /// Rollenauskunft sähe von einem entzogenen Recht sonst nicht zu
    /// unterscheiden aus. 503 heisst „hat nicht geantwortet", 403 heisst „nein";
    /// dieselbe Unterscheidung trifft dieser Baum beim Einwilligungs-Ledger.
    /// </remarks>
    public const string Schweigt = "workertransfer.rollenauskunft.schweigt";

    private readonly AuthorizationMiddlewareResultHandler _weiter = new();

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Challenged)
        {
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            if (context.Items.ContainsKey(Schweigt))
            {
                await ProblemDetailsMiddleware.Schreibe(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the role lookup did not answer");
                return;
            }

            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "not permitted");
            return;
        }

        await _weiter.HandleAsync(next, context, policy, authorizeResult);
    }
}
