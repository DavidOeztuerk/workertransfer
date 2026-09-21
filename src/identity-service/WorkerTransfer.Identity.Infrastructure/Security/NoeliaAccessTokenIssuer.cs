using Noelia.Core.Identity;
using Noelia.Infrastructure.Security;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>
/// Issues the access token through Noelia, in the shape both worlds read.
/// </summary>
/// <remarks>
/// Noelia writes <c>sub</c>, <c>email</c>, <c>jti</c>, <c>iat</c>, <c>exp</c>,
/// <c>iss</c>, <c>aud</c>, <c>session_id</c> and — while acting for a company —
/// <c>tenant</c>.
/// <para>
/// And <b>one</b> more, unasked:
/// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
/// — the same id as <c>sub</c>, once again. It stays, and that is measured:
/// Noelia verifies tokens with <c>MapInboundClaims = false</c>, so it does not
/// derive it from <c>sub</c>, and seventeen readers in Noelia resolve the
/// caller through it — two of them in provider packages. Some seventy bytes
/// saved, seventeen silent nulls gained.
/// </para>
/// <para>
/// <b>Until Girder 4.1.0 there were three.</b> <c>email_verified</c> and
/// <c>account_status</c> stood in every token and <b>always</b> carried the
/// same values, <c>false</c> and <c>"Active"</c>, because <c>UserClaims</c>
/// pre-set them that way and nobody sets them here. An account whose address
/// had just been confirmed carried <c>email_verified: false</c>; a suspended
/// one carried <c>account_status: "Active"</c>. Noelia's own policies
/// <c>EmailVerifiedHandler</c> and <c>ActiveAccountHandler</c> read exactly
/// these claims — so the one locked everybody out, the other let everybody
/// through. Since 4.1.0 both fields are <c>bool?</c> and <c>string?</c> with no
/// default: unsaid means not written, and both checkers refuse for want of a
/// claim.
/// </para>
/// <para>
/// It cost us nothing, because we use neither policy and the account status
/// comes out of the database per request. <b>It was never worked around</b> —
/// filtering the claims away here would mean building past Noelia's output, and
/// the detour would now have stayed standing.
/// <see cref="WorkerTransfer.Identity.Tests"/> pins the closed set, no longer
/// single names: what stands here is complete from now on.
/// </para>
/// <para>
/// During the migration two more stood here: <c>tenant_id</c> beside Noelia's
/// <c>tenant</c>, and <c>type: "access"</c>. Python's <c>TokenPayload</c>
/// demanded both. They are gone — <c>tenant_id</c> beside <c>tenant</c> was
/// exactly the duplication where one day one of the two gets maintained and the
/// other does not.
/// </para>
/// <para>
/// Roles and permissions are not written. They are read from
/// <c>user_tenant_memberships</c> per operation, never from a token — so the
/// token was never authoritative about them. Since Girder 4.1.0 they
/// <em>could</em> be written as a list (<c>UserClaims.CustomClaimArrays</c>);
/// that we do not do it is therefore a decision again rather than a limit of
/// the library. A permission in the token would take effect on a withdrawal
/// only at expiry.
/// </para>
/// </remarks>
public sealed class NoeliaAccessTokenIssuer(IJwtService jwt) : IAccessTokenIssuer
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
