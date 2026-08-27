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
/// <c>tenant</c>. Mehr steht nicht drin.
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
