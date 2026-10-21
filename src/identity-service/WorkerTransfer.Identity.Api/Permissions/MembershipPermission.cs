using System.Security.Claims;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using WorkerTransfer.Identity.Application.Companies;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Api.Permissions;

/// <summary>
/// Answers a company permission out of the <strong>membership table</strong>,
/// not out of the token.
/// </summary>
/// <remarks>
/// <para><strong>Why beside and not instead.</strong> ASP.NET runs <em>all</em>
/// handlers for one requirement; one <c>Succeed</c> is enough. Noelia's
/// <c>PermissionAuthorizationHandler</c> therefore stays standing and goes on
/// reading claims — it only finds none of ours, because our token carries none.
/// This one answers out of the database instead.</para>
///
/// <para><strong>And why not simply put permissions in the token.</strong> That
/// would be the shorter way and the worse one: a token lives on after somebody
/// was removed from a company. The removal would then take effect only on
/// expiry — at exactly the operation where immediately is the only thing that
/// counts. From the same family as "a consent must take effect immediately":
/// that holds because it is right.</para>
///
/// <para>The price is one query per protected request. It hits a primary key
/// and touches only a company's administrative endpoints — not the read paths
/// people use all day.</para>
///
/// <para><strong>The company comes from the path, and that is right.</strong>
/// At first the tenant from the token stood here — stricter, but wrong: it
/// would have demanded that somebody call <c>POST /auth/company/{id}</c>
/// beforehand, and the existing journeys do not. Nor is the path a question of
/// trust here: it only names <em>which</em> company is meant. What is checked is
/// <em>the caller's</em> membership <em>in exactly that</em> company — whoever
/// asks for a foreign one has no role there and is refused. That is exactly how
/// <c>CompanyAccess.AsAdminAsync</c> read it before.</para>
/// </remarks>
public sealed class MembershipPermission(
    CompanyAccess access,
    IHttpContextAccessor accessor)
    : AuthorizationHandler<PermissionRequirement>
{
    private static readonly string[] AdminOnly =
    [
        CompanyPermissions.Invite,
        CompanyPermissions.WithdrawInvitation,
        CompanyPermissions.Remove,
        CompanyPermissions.Ai
    ];

    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!AdminOnly.Contains(requirement.Permission, StringComparer.Ordinal))
        {
            return;
        }

        if (Subject(context.User) is not { } actor || Company() is not { } company)
        {
            // No Succeed and no Fail: there is nothing to decide here — and
            // another handler may still answer.
            return;
        }

        var role = await access.RoleOrNoneAsync(actor, company);

        if (role == MembershipRole.Admin)
        {
            context.Succeed(requirement);
        }
    }

    /// <summary>
    /// Who is asking — out of the <c>ClaimsPrincipal</c> the authorisation holds.
    /// </summary>
    /// <remarks>
    /// NOT out of <c>ICurrentPrincipal</c>. That was the first attempt and gave
    /// a 403 at every protected request: Noelia's <c>UseAuth()</c> is
    /// authentication <em>and</em> authorisation, and <c>UsePrincipal()</c> runs
    /// after it — so while the policy is deciding, <c>ICurrentPrincipal.Current</c>
    /// is still empty. An authorisation handler takes the principal it is given.
    /// </remarks>
    private static SubjectId? Subject(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? new SubjectId(id) : null;
    }

    /// <summary>Which company the path names.</summary>
    private TenantId? Company() =>
        accessor.HttpContext?.GetRouteValue("tenantId") is string raw
        && Guid.TryParse(raw, out var id)
            ? new TenantId(id)
            : null;
}
