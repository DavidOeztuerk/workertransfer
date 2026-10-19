using System.Security.Claims;
using Noelia.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.ServiceDefaults.Roles;

/// <summary>
/// Answers a company permission out of identity-service's
/// <strong>membership table</strong> — in every service except identity itself.
/// </summary>
/// <remarks>
/// <para><strong>Why beside and not instead.</strong> ASP.NET runs
/// <em>all</em> handlers for one requirement; a single <c>Succeed</c> is
/// enough. Noelia's <c>PermissionAuthorizationHandler</c> therefore stays and
/// goes on reading claims — with us it finds none, because our token carries
/// none. This one answers out of the table instead.</para>
///
/// <para><strong>The company comes from the TOKEN, not from the path.</strong>
/// That is the difference to <c>MembershipPermission</c> in identity-service,
/// and it follows from the addresses: there the company stands in the path
/// (<c>/companies/{tenantId}/…</c>), here somebody acts for the company that
/// stands in the token (<c>/jobs/{id}/publish</c> names none). The tenant in
/// the token never came from caller input — it is granted by
/// <c>POST /auth/company/{id}</c> after the server has checked the membership
/// (ADR-0018).</para>
///
/// <para><strong>No cache.</strong> The price is one lookup per protected
/// request, and it touches only the operations that bind a company — never the
/// reads people use all day.</para>
///
/// <para><strong>When the lookup is silent, nothing is refused; it is said that
/// nobody answers.</strong> The handler can only say "yes", so it leaves a note
/// on the <c>HttpContext</c>, and <see cref="RejectionShape"/> turns that into
/// 503 rather than 403. Without it an outage of identity-service would be
/// indistinguishable from a permission that was taken away — and the person in
/// front of it would read "you may not" where "we do not know just now" is the
/// truth.</para>
/// </remarks>
public sealed class AdminPermission(
    AdminPermissions permissions,
    ICompanyRoles roles,
    IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!permissions.RequiresAdmin(requirement.Permission))
        {
            // No Succeed and no Fail: about this permission this service has
            // nothing to say — and another handler may still answer.
            return;
        }

        if (ActingSubject(context.User) is not { } actor || ActingCompany(context.User) is not { } company)
        {
            return;
        }

        try
        {
            if (await roles.RoleAsync(actor, company) == CompanyRole.Admin)
            {
                context.Succeed(requirement);
            }
        }
        catch (RoleLookupSilent)
        {
            if (httpContextAccessor.HttpContext is { } current)
            {
                current.Items[RejectionShape.Silent] = true;
            }
        }
    }

    /// <summary>
    /// Who is asking — out of the <c>ClaimsPrincipal</c> the authorization
    /// holds.
    /// </summary>
    /// <remarks>
    /// NOT out of <c>ICurrentPrincipal</c>: Noelia's <c>UseAuth()</c> is
    /// authentication <em>and</em> authorization, and <c>UsePrincipal()</c>
    /// runs after it — so while the policy is deciding,
    /// <c>ICurrentPrincipal.Current</c> is still empty. An authorization
    /// handler takes the principal it is given.
    /// </remarks>
    private static Guid? ActingSubject(ClaimsPrincipal user)
    {
        var raw = user.FindFirst("sub")?.Value
                  ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>Which company is acted for — the claim out of the token.</summary>
    private static Guid? ActingCompany(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("tenant")?.Value, out var id) ? id : null;
}
