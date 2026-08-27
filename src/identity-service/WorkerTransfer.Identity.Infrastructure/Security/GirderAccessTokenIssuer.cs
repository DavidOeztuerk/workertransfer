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
/// <c>tenant</c>. Two more are added for the Python services that are still
/// live: <c>tenant_id</c> and <c>type</c>. Both leave with the transition and
/// are listed as Ü-3 in <c>docs/uebergang-python-dotnet.md</c>.
/// <para>
/// Roles and permissions are not written. They are read from
/// <c>user_tenant_memberships</c> per operation, never from a token, and
/// <c>CustomClaims</c> could only carry them as a string — see
/// <c>bugs/customclaims-kann-keine-liste-ausdruecken.md</c>.
/// </para>
/// </remarks>
public sealed class GirderAccessTokenIssuer(IJwtService jwt) : IAccessTokenIssuer
{
    private const string AccessTokenType = "access";

    /// <inheritdoc />
    public async Task<string> IssueAsync(
        SubjectId subject,
        string email,
        Capacity acting,
        SessionId session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acting);

        var custom = new Dictionary<string, string> { ["type"] = AccessTokenType };

        if (acting is Capacity.ForCompany company)
        {
            custom["tenant_id"] = company.Tenant.ToString();
        }

        var issued = await jwt.GenerateTokenAsync(new UserClaims
        {
            UserId = subject.ToString(),
            Email = email,
            Acting = acting,
            SessionId = session.ToString(),
            CustomClaims = custom
        });

        return issued.AccessToken;
    }
}
