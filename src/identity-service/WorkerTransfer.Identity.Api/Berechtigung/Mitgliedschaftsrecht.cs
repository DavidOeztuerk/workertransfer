using System.Security.Claims;
using Girder.Core.Identity;
using Girder.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using WorkerTransfer.Identity.Application.Unternehmen;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Api.Berechtigung;

/// <summary>
/// Beantwortet ein Firmenrecht aus der <strong>Mitgliedschaftstabelle</strong>,
/// nicht aus dem Token.
/// </summary>
/// <remarks>
/// <para><strong>Warum daneben und nicht statt.</strong> ASP.NET führt
/// <em>alle</em> Handler zu einer Anforderung aus; ein <c>Succeed</c> genügt.
/// Girders <c>PermissionAuthorizationHandler</c> bleibt also stehen und liest
/// weiter Ansprüche — er findet bei uns nur keine, weil unser Token keine
/// trägt. Dieser hier antwortet stattdessen aus der Datenbank.</para>
///
/// <para><strong>Und warum nicht einfach Rechte ins Token.</strong> Das wäre der
/// kürzere Weg und der schlechtere: ein Token lebt weiter, nachdem jemand aus
/// einer Firma entfernt wurde. Die Entfernung wirkte dann erst beim Ablauf —
/// bei genau der Handlung, bei der sofort das Einzige ist, was zählt. Aus
/// derselben Familie wie „eine Einwilligung muss sofort wirken": das gilt,
/// weil es richtig ist.</para>
///
/// <para>Der Preis ist eine Abfrage je geschützter Anfrage. Sie trifft einen
/// Primärschlüssel und betrifft nur die Verwaltungsendpunkte einer Firma —
/// nicht die Lesewege, die Menschen den ganzen Tag benutzen.</para>
///
/// <para><strong>Die Firma kommt aus dem Pfad, und das ist richtig so.</strong>
/// Erst stand hier der Mandant aus dem Token — strenger, aber falsch: es hätte
/// verlangt, dass jemand vorher <c>POST /auth/company/{id}</c> ruft, und die
/// bestehenden Reisen tun das nicht. Der Pfad ist hier auch keine Vertrauens-
/// frage: er benennt nur, <em>welche</em> Firma gemeint ist. Geprüft wird die
/// Mitgliedschaft <em>des Aufrufers in genau dieser</em> Firma — wer für eine
/// fremde fragt, hat dort keine Rolle und wird abgelehnt. Genau so las es
/// <c>Firmenzugriff.AlsAdminAsync</c> schon vorher.</para>
/// </remarks>
public sealed class Mitgliedschaftsrecht(
    Firmenzugriff zugriff,
    IHttpContextAccessor zugang)
    : AuthorizationHandler<PermissionRequirement>
{
    private static readonly string[] NurAdmin =
    [
        Firmenrechte.Einladen,
        Firmenrechte.EinladungZuruecknehmen,
        Firmenrechte.Entfernen
    ];

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!NurAdmin.Contains(requirement.Permission, StringComparer.Ordinal))
        {
            return;
        }

        if (Wer(context.User) is not { } handelnder || Firma() is not { } firma)
        {
            // Kein Succeed und kein Fail: hier ist nichts zu entscheiden — und
            // ein anderer Handler darf trotzdem noch antworten.
            return;
        }

        var rolle = await zugriff.RolleOderNichtsAsync(handelnder, firma);

        if (rolle == MembershipRole.Admin)
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Wer fragt — aus dem <c>ClaimsPrincipal</c>, den die Autorisierung hält.
    /// </summary>
    /// <remarks>
    /// NICHT aus <c>ICurrentPrincipal</c>. Das war der erste Versuch und ergab
    /// an jeder geschützten Anfrage einen 403: Girders <c>UseAuth()</c> ist
    /// Authentifizierung <em>und</em> Autorisierung, und <c>UsePrincipal()</c>
    /// läuft danach — während die Richtlinie entscheidet, ist
    /// <c>ICurrentPrincipal.Current</c> also noch leer. Ein
    /// Autorisierungshandler nimmt den Principal, den er bekommt.
    /// </remarks>
    private static SubjectId? Wer(ClaimsPrincipal nutzer)
    {
        var roh = nutzer.FindFirst("sub")?.Value
                  ?? nutzer.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(roh, out var kennung) ? new SubjectId(kennung) : null;
    }

    /// <summary>Welche Firma der Pfad benennt.</summary>
    private TenantId? Firma() =>
        zugang.HttpContext?.GetRouteValue("tenantId") is string roh
        && Guid.TryParse(roh, out var kennung)
            ? new TenantId(kennung)
            : null;
}
