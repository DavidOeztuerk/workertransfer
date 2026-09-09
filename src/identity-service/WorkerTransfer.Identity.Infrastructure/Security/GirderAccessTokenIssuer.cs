using Girder.Core.Identity;
using Girder.Infrastructure.Security;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>
/// Issues the access token through Girder, in the shape both worlds read.
/// </summary>
/// <remarks>
/// Girder writes <c>sub</c>, <c>email</c>, <c>jti</c>, <c>iat</c>, <c>exp</c>,
/// <c>iss</c>, <c>aud</c>, <c>session_id</c> and — while acting for a company —
/// <c>tenant</c>.
/// <para>
/// Dazu <b>einen</b> weiteren, unaufgefordert:
/// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
/// — dieselbe Kennung wie <c>sub</c>, noch einmal. Er bleibt, und das ist
/// gemessen: Girder prüft Token mit <c>MapInboundClaims = false</c>, leitet ihn
/// also nicht aus <c>sub</c> ab, und siebzehn Leser in Girder lösen den
/// Aufrufer darüber auf — zwei davon in Anbieterpaketen. Rund siebzig Bytes
/// gespart, siebzehn stille Nulls gewonnen.
/// </para>
/// <para>
/// <b>Bis Girder 4.1.0 waren es drei.</b> <c>email_verified</c> und
/// <c>account_status</c> standen in jedem Token und trugen <b>immer</b>
/// dieselben Werte, <c>false</c> und <c>"Active"</c>, weil <c>UserClaims</c>
/// sie so vorbelegte und niemand sie hier setzt. Ein Konto, dessen Adresse
/// gerade bestätigt wurde, trug <c>email_verified: false</c>; ein gesperrtes
/// trug <c>account_status: "Active"</c>. Girders eigene Richtlinien
/// <c>EmailVerifiedHandler</c> und <c>ActiveAccountHandler</c> lesen genau
/// diese Ansprüche — die eine sperrte damit jeden aus, die andere ließ jeden
/// durch. Seit 4.1.0 sind beide Felder <c>bool?</c> und <c>string?</c> ohne
/// Vorgabe: ungesagt heißt nicht geschrieben, und beide Prüfer lehnen mangels
/// Anspruch ab.
/// </para>
/// <para>
/// Uns kostete es nichts, weil wir beide Richtlinien nicht benutzen und der
/// Kontostand je Anfrage aus der Datenbank kommt. <b>Umgangen wurde es nie</b>
/// — die Ansprüche hier wegzufiltern hiesse, an Girders Ausgabe vorbeizubauen,
/// und der Umweg wäre jetzt stehen geblieben.
/// <see cref="WorkerTransfer.Identity.Tests"/> nagelt die geschlossene Menge
/// fest, nicht mehr einzelne Namen: was hier steht, ist ab jetzt vollständig.
/// </para>
/// <para>
/// Zur Uebergangszeit standen hier zwei weitere: <c>tenant_id</c> neben Girders
/// <c>tenant</c>, und <c>type: "access"</c>. Pythons <c>TokenPayload</c>
/// verlangte beides. Sie sind weg — <c>tenant_id</c> neben <c>tenant</c> war
/// genau die Doppelung, bei der eines Tages eines von beiden gepflegt wird und
/// das andere nicht.
/// </para>
/// <para>
/// Roles and permissions are not written. They are read from
/// <c>user_tenant_memberships</c> per operation, never from a token — so the
/// token was never authoritative about them. Seit Girder 4.1.0 <em>ließen</em>
/// sie sich als Liste schreiben (<c>UserClaims.CustomClaimArrays</c>); dass wir
/// es nicht tun, ist damit wieder eine Entscheidung statt einer Grenze der
/// Bibliothek. Ein Recht im Token wirkte bei einer Entziehung erst beim Ablauf.
/// </para>
/// </remarks>
public sealed class GirderAccessTokenIssuer(IJwtService jwt) : IAccessTokenIssuer
{
    /// <inheritdoc />
    public async Task<string> IssueAsync(
        SubjectId subject,
        string email,
        Capacity acting,
        SessionId session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acting);

        var issued = await jwt.GenerateTokenAsync(new UserClaims
        {
            UserId = subject.ToString(),
            Email = email,
            Acting = acting,
            SessionId = session.ToString()
        });

        return issued.AccessToken;
    }
}
