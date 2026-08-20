using Microsoft.IdentityModel.Tokens;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>
/// Widens issuer and audience validation to the tokens the Python
/// identity-service issued, which carry neither claim.
/// </summary>
/// <remarks>
/// A stated rule rather than a switched-off check: an *absent* claim is the
/// legacy issuer and passes; a *present* one must be ours. Turning
/// <see cref="TokenValidationParameters.ValidateIssuer"/> and
/// <see cref="TokenValidationParameters.ValidateAudience"/> off instead would
/// also accept a token minted for somebody else entirely.
/// <para>
/// Listed as Ü-2 in <c>docs/uebergang-python-dotnet.md</c>.
/// </para>
/// </remarks>
public static class UebergangsTokenValidation
{
    /// <summary>
    /// Accepts a token from either issuer, and nothing beyond the two.
    /// </summary>
    /// <param name="parameters">Parameters built from the key ring.</param>
    /// <returns>The same instance, with both validators set.</returns>
    public static TokenValidationParameters FuerBeideAussteller(
        this TokenValidationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var erwarteterAussteller = parameters.ValidIssuer;
        var erwarteteZielgruppe = parameters.ValidAudience;

        parameters.AudienceValidator = (audiences, _, _) =>
            audiences is null
            || !audiences.Any()
            || audiences.Contains(erwarteteZielgruppe, StringComparer.Ordinal);

        parameters.IssuerValidator = (issuer, _, _) =>
            string.IsNullOrEmpty(issuer) || issuer == erwarteterAussteller
                ? issuer ?? string.Empty
                : throw new SecurityTokenInvalidIssuerException(
                    $"The token names issuer '{issuer}'.");

        return parameters;
    }
}
