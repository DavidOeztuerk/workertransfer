using System.Security.Claims;
using Girder.Core.Identity;
using Girder.Infrastructure.Security.Identity;
using Microsoft.IdentityModel.JsonWebTokens;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>
/// Builds a <see cref="Principal"/> from a token issued by either world.
/// </summary>
/// <remarks>
/// Replaces Girder's own factory for as long as tokens from the Python
/// identity-service are still in circulation — it reads <c>tenant</c>, and
/// Python writes <c>tenant_id</c>. Registered after
/// <c>AddSharedInfrastructure</c>, which registers Girder's with a plain
/// <c>AddSingleton</c>, so the later registration is the one resolved.
/// <para>
/// Listed as Ü-2 in <c>docs/uebergang-python-dotnet.md</c>: it goes when the
/// last Python service does.
/// </para>
/// </remarks>
public sealed class UebergangsPrincipalFactory : IPrincipalFactory
{
    private const string GirderTenantClaim = "tenant";
    private const string PythonTenantClaim = "tenant_id";
    private const string TokenTypeClaim = "type";
    private const string AccessTokenType = "access";

    /// <inheritdoc />
    public PrincipalResult Create(ClaimsPrincipal? user)
    {
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return PrincipalResult.Anonymous();
        }

        var subjectClaim = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!SubjectId.TryParse(subjectClaim, out var subject))
        {
            return PrincipalResult.Invalid("The token carries no usable subject claim.");
        }

        if (user.FindFirst(TokenTypeClaim) is { } type && type.Value != AccessTokenType)
        {
            return PrincipalResult.Invalid(
                $"The token states type '{type.Value}' and is not an access token.");
        }

        return Acting(user, out var acting, out var error)
            ? PrincipalResult.Success(new Principal { Subject = subject, Acting = acting })
            : PrincipalResult.Invalid(error);
    }

    /// <summary>
    /// Resolves the capacity from either tenant claim.
    /// </summary>
    /// <remarks>
    /// A claim that is present and unreadable is refused rather than downgraded
    /// to a person: the two differ in which data the caller then sees, and a
    /// downgrade says so nowhere.
    /// <para>
    /// One value is not a claim at all. Python writes <c>"tenant_id": null</c>
    /// to say "acting as a person", and that arrives as a present, empty claim
    /// typed <see cref="JsonClaimValueTypes.JsonNull"/>.
    /// </para>
    /// </remarks>
    private static bool Acting(ClaimsPrincipal user, out Capacity acting, out string error)
    {
        acting = Capacity.AsSelf.Instance;
        error = string.Empty;

        TenantId? resolved = null;

        foreach (var name in new[] { GirderTenantClaim, PythonTenantClaim })
        {
            var claim = user.FindFirst(name);

            if (claim is null || claim.ValueType == JsonClaimValueTypes.JsonNull)
            {
                continue;
            }

            if (!TenantId.TryParse(claim.Value, out var tenant))
            {
                error = $"The token carries an unusable '{name}' claim.";
                return false;
            }

            if (resolved is { } already && already != tenant)
            {
                error = "The token names two different companies.";
                return false;
            }

            resolved = tenant;
        }

        if (resolved is { } company)
        {
            acting = new Capacity.ForCompany(company);
        }

        return true;
    }
}
