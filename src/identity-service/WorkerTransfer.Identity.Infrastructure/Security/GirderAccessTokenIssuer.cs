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
/// <b>Hier stand „Mehr steht nicht drin", und das war falsch.</b> Girder legt
/// drei weitere Ansprüche dazu, unaufgefordert und ohne dass eine Zeile hier
/// sie nennt (gemessen an einem echten Token, in beiden Handlungsformen):
/// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
/// — dieselbe Kennung wie <c>sub</c>, noch einmal —, sowie
/// <c>email_verified</c> und <c>account_status</c>.
/// </para>
/// <para>
/// Die letzten beiden sind schlimmer als überflüssig: sie tragen <b>immer</b>
/// dieselben Werte, <c>false</c> und <c>"Active"</c>, weil
/// <c>UserClaims</c> sie so vorbelegt und niemand sie hier setzt. Ein Konto,
/// dessen Adresse gerade bestätigt wurde, trägt <c>email_verified: false</c>;
/// ein gesperrtes trägt <c>account_status: "Active"</c>. Girders eigene
/// Richtlinien <c>EmailVerifiedHandler</c> und <c>ActiveAccountHandler</c>
/// lesen genau diese Ansprüche — wer sie einschaltet, sperrt entweder alle aus
/// oder lässt Gesperrte durch.
/// </para>
/// <para>
/// Uns kostet es heute nichts: wir benutzen beide Richtlinien nicht, und der
/// Kontostand wird je Anfrage aus der Datenbank gelesen. Gemeldet als
/// <c>bugs/token-traegt-zwei-konstante-luegen.md</c>. <b>Kein Umweg hier</b> —
/// die Ansprüche wegzufiltern hiesse, an Girders Ausgabe vorbeizubauen, und der
/// Umweg bliebe stehen, wenn der Fehler behoben ist.
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
/// <c>user_tenant_memberships</c> per operation, never from a token, and
/// <c>CustomClaims</c> could only carry them as a string — see
/// <c>bugs/customclaims-kann-keine-liste-ausdruecken.md</c>.
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
