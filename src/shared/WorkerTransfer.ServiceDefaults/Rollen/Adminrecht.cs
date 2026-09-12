using System.Security.Claims;
using Girder.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.ServiceDefaults.Rollen;

/// <summary>
/// Beantwortet ein Firmenrecht aus der <strong>Mitgliedschaftstabelle</strong>
/// von identity-service — in jedem Dienst ausser identity selbst.
/// </summary>
/// <remarks>
/// <para><strong>Warum daneben und nicht statt.</strong> ASP.NET führt
/// <em>alle</em> Handler zu einer Anforderung aus; ein <c>Succeed</c> genügt.
/// Girders <c>PermissionAuthorizationHandler</c> bleibt also stehen und liest
/// weiter Ansprüche — er findet bei uns keine, weil unser Token keine trägt.
/// Dieser hier antwortet stattdessen aus der Tabelle.</para>
///
/// <para><strong>Die Firma kommt aus dem TOKEN, nicht aus dem Pfad.</strong>
/// Das ist der Unterschied zu <c>Mitgliedschaftsrecht</c> in identity-service,
/// und er folgt aus den Adressen: dort steht die Firma im Pfad
/// (<c>/companies/{tenantId}/…</c>), hier handelt jemand für die Firma, die im
/// Token steht (<c>/jobs/{id}/publish</c> nennt keine). Der Mandant im Token
/// kam nie aus einer Eingabe des Aufrufers — er wird von
/// <c>POST /auth/company/{id}</c> vergeben, nachdem der Server die
/// Mitgliedschaft geprüft hat (ADR-0018).</para>
///
/// <para><strong>Kein Zwischenspeicher.</strong> Der Preis ist eine Abfrage je
/// geschützter Anfrage, und sie trifft nur die Handlungen, die ein Unternehmen
/// binden — nicht die Lesewege, die Menschen den ganzen Tag benutzen.</para>
///
/// <para><strong>Wenn die Auskunft schweigt, wird nicht abgelehnt, sondern
/// gesagt, dass niemand antwortet.</strong> Der Handler kann nur „ja" sagen,
/// also hinterlässt er eine Notiz am <c>HttpContext</c>, und
/// <see cref="Ablehnungsgestalt"/> macht daraus 503 statt 403. Ohne das wäre
/// ein Ausfall von identity-service von einem entzogenen Recht nicht zu
/// unterscheiden — und der Mensch davor läse „du darfst nicht", wo „wir wissen
/// es gerade nicht" die Wahrheit ist.</para>
/// </remarks>
public sealed class Adminrecht(
    Adminrechte rechte,
    IFirmenrollen rollen,
    IHttpContextAccessor zugang)
    : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!rechte.NurAdmin(requirement.Permission))
        {
            // Kein Succeed und kein Fail: über dieses Recht hat dieser Dienst
            // nichts zu sagen — und ein anderer Handler darf trotzdem noch
            // antworten.
            return;
        }

        if (Wer(context.User) is not { } handelnder || Firma(context.User) is not { } firma)
        {
            return;
        }

        try
        {
            if (await rollen.RolleAsync(handelnder, firma) == Firmenrolle.Admin)
            {
                context.Succeed(requirement);
            }
        }
        catch (RolleSchweigt)
        {
            if (zugang.HttpContext is { } laufende)
            {
                laufende.Items[Ablehnungsgestalt.Schweigt] = true;
            }
        }
    }

    /// <summary>
    /// Wer fragt — aus dem <c>ClaimsPrincipal</c>, den die Autorisierung hält.
    /// </summary>
    /// <remarks>
    /// NICHT aus <c>ICurrentPrincipal</c>: Girders <c>UseAuth()</c> ist
    /// Authentifizierung <em>und</em> Autorisierung, und <c>UsePrincipal()</c>
    /// läuft danach — während die Richtlinie entscheidet, ist
    /// <c>ICurrentPrincipal.Current</c> also noch leer. Ein
    /// Autorisierungshandler nimmt den Principal, den er bekommt.
    /// </remarks>
    private static Guid? Wer(ClaimsPrincipal nutzer)
    {
        var roh = nutzer.FindFirst("sub")?.Value
                  ?? nutzer.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(roh, out var kennung) ? kennung : null;
    }

    /// <summary>Für welche Firma gehandelt wird — der Anspruch aus dem Token.</summary>
    private static Guid? Firma(ClaimsPrincipal nutzer) =>
        Guid.TryParse(nutzer.FindFirst("tenant")?.Value, out var kennung) ? kennung : null;
}
