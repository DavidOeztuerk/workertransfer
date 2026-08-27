using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Girder.Core.Identity;
using Girder.Infrastructure.Security.Identity;
using Girder.Infrastructure.Security.Keys;
using Microsoft.IdentityModel.Tokens;
using WorkerTransfer.Identity.Infrastructure.Security;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// A token the Python service issued, through the verification chain this
/// service runs — parameters first, then the principal.
/// </summary>
/// <remarks>
/// The half of the proof that runs here. The other direction — a token from
/// this service through <c>TokenManager.verify_token</c> — is
/// <c>scripts/token-kreuzbeweis.sh</c>.
/// </remarks>
public class KreuzbeweisPythonNachDotnetTests
{
    private static readonly UebergangsPrincipalFactory Factory = new();

    private static TokenValidationParameters Parameters()
    {
        var shared = SigningKey.FromSharedSecret(EchtePythonToken.Secret, kid: null);
        var parameters = new KeyRing([shared], null)
            .ValidationParameters(Tokenform.Issuer, Tokenform.Audience);

        return parameters.FuerBeideAussteller();
    }

    private static PrincipalResult Pruefe(string token) =>
        Factory.Create(new JwtSecurityTokenHandler().ValidateToken(token, Parameters(), out _));

    [Fact]
    public void Ein_Personen_Token_aus_Python_wird_angenommen_und_bleibt_eine_Person()
    {
        var result = Pruefe(EchtePythonToken.AlsPerson);

        result.IsInvalid.Should().BeFalse();
        result.Principal!.Subject.Should().Be(new SubjectId(EchtePythonToken.Anna));
        result.Principal.Acting.Should().BeOfType<Capacity.AsSelf>();
    }

    [Fact]
    public void Ein_Firmen_Token_aus_Python_bleibt_ein_Firmen_Token()
    {
        var result = Pruefe(EchtePythonToken.FuerDieFirma);

        result.IsInvalid.Should().BeFalse();
        result.Principal!.Acting.Should().BeOfType<Capacity.ForCompany>()
            .Which.Tenant.Should().Be(new TenantId(EchtePythonToken.Firma));
    }

    [Fact]
    public async Task Ein_Token_aus_diesem_Dienst_wird_ebenso_angenommen()
    {
        var firma = TenantId.New();
        var eigener = await Tokenform.Issuer_().IssueAsync(
            SubjectId.New(), "anna@example.com", new Capacity.ForCompany(firma), SessionId.New());

        var shared = SigningKey.FromSharedSecret(Tokenform.Secret, kid: null);
        var claims = new JwtSecurityTokenHandler().ValidateToken(
            eigener,
            new KeyRing([shared], null)
                .ValidationParameters(Tokenform.Issuer, Tokenform.Audience)
                .FuerBeideAussteller(),
            out _);

        Factory.Create(claims).Principal!.Acting.Should().BeOfType<Capacity.ForCompany>()
            .Which.Tenant.Should().Be(firma);
    }

    public class DieNachsichtGiltNurDemFehlen
    {
        private static void Erwarte_Ablehnung(string issuer, string audience)
        {
            var shared = SigningKey.FromSharedSecret(Tokenform.Secret, kid: null);
            var fremder = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                expires: DateTime.UtcNow.AddMinutes(15),
                claims: [new System.Security.Claims.Claim("sub", Guid.NewGuid().ToString())],
                signingCredentials: shared.SigningCredentials()));

            var pruefen = () => new JwtSecurityTokenHandler().ValidateToken(
                fremder,
                new KeyRing([shared], null)
                    .ValidationParameters(Tokenform.Issuer, Tokenform.Audience)
                    .FuerBeideAussteller(),
                out _);

            pruefen.Should().Throw<SecurityTokenException>();
        }

        [Fact]
        public void Eine_fremde_Zielgruppe_wird_abgelehnt()
            => Erwarte_Ablehnung(Tokenform.Issuer, "ein-anderes-system");

        [Fact]
        public void Ein_fremder_Aussteller_wird_abgelehnt()
            => Erwarte_Ablehnung("ein-anderer-aussteller", Tokenform.Audience);
    }
}
