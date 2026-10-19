using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>
/// Gives a refusal by a policy the same shape as every other error.
/// </summary>
/// <remarks>
/// <para>Without this, a failed authorization falls out as a <strong>bare
/// 401/403 with an empty body</strong>: it does not throw, it short-circuits
/// the answer — so <c>ProblemDetailsMiddleware</c> never sees it. The interface
/// would then get two shapes for the same thing, depending on whether a
/// hand-written check or a policy refused, and a person's report would carry no
/// correlation id.</para>
///
/// <para><strong>Here and not in identity-service.</strong> It stood there for
/// as long as identity was the only service with policies. Since the company
/// permissions hang in four services, it is exactly what <c>ServiceDefaults</c>
/// carries: one shape across all services, so a caller does not have to know
/// who answered in order to read the error.</para>
///
/// <para>The message says only that it was not permitted. <em>Which</em> policy
/// was missing deliberately does not stand in it: that would be a map of the
/// permissions for anybody who queries them.</para>
/// </remarks>
public sealed class RejectionShape : IAuthorizationMiddlewareResultHandler
{
    /// <summary>
    /// The note with which a handler says: it was not the permission, it was
    /// that nobody answered.
    /// </summary>
    /// <remarks>
    /// An authorization handler can only say "yes" — a silent role lookup would
    /// otherwise be indistinguishable from a permission that was taken away.
    /// 503 means "did not answer", 403 means "no"; this tree makes the same
    /// distinction at the consent ledger.
    /// </remarks>
    public const string Silent = "workertransfer.role-lookup.silent";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

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
            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status401Unauthorized,
                "Request failed", "not authenticated");
            return;
        }

        if (authorizeResult.Forbidden)
        {
            // web/src/shared/api/errors.ts (ADMIN_CASES) matches both sentences below.
            if (context.Items.ContainsKey(Silent))
            {
                await ProblemDetailsMiddleware.Write(
                    context, StatusCodes.Status503ServiceUnavailable,
                    "Request failed", "the role lookup did not answer");
                return;
            }

            await ProblemDetailsMiddleware.Write(
                context, StatusCodes.Status403Forbidden,
                "Request failed", "not permitted");
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
