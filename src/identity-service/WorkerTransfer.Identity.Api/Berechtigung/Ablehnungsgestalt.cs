using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Api.Berechtigung;

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
/// <para>Der Grund, es überhaupt zu bemerken: derselbe wie bei allem hier —
/// eine Gestalt über alle Dienste, damit ein Aufrufer nicht wissen muss, wer
/// geantwortet hat, um den Fehler zu lesen.</para>
///
/// <para>Die Meldung sagt nur, dass es nicht erlaubt war. <em>Welche</em>
/// Richtlinie fehlte, steht bewusst nicht darin: das wäre eine Landkarte der
/// Rechte für jeden, der sie abfragt.</para>
/// </remarks>
public sealed class Ablehnungsgestalt : IAuthorizationMiddlewareResultHandler
{
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
            await ProblemDetailsMiddleware.Schreibe(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "not permitted");
            return;
        }

        await _weiter.HandleAsync(next, context, policy, authorizeResult);
    }
}
