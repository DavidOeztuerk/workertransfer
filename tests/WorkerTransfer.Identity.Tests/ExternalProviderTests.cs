using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Security;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The provider side of ADR-0053 — the redirect, the exchange and the id token
/// check, against answers shaped like the discovery documents measured on
/// 18.10.2026. No network.
/// </summary>
public sealed class ExternalProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static ExternalLoginSettings Settings() => new()
    {
        CallbackBase = "http://localhost:8090",
        Google = new()
        {
            ClientId = "google-client", ClientSecret = "google-secret",
            AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenEndpoint = "https://oauth2.googleapis.com/token",
            Issuer = "https://accounts.google.com"
        },
        Microsoft = new()
        {
            ClientId = "ms-client", ClientSecret = "ms-secret",
            AuthorizationEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
            Issuer = "https://login.microsoftonline.com/{tenantid}/v2.0"
        },
        Apple = new()
        {
            ClientId = "com.example.web", TeamId = "TEAM123456", KeyId = "KEY1234567", PrivateKey = AppleKey.Pkcs8,
            AuthorizationEndpoint = "https://appleid.apple.com/auth/authorize",
            TokenEndpoint = "https://appleid.apple.com/auth/token",
            Issuer = "https://appleid.apple.com"
        }
    };

    private static HttpExternalProviders Providers(Wire? wire = null, ExternalLoginSettings? settings = null) =>
        new(new Factory(wire ?? new Wire(HttpStatusCode.OK, "{}")), Options.Create(settings ?? Settings()),
            new FixedClock(Now), NullLogger<HttpExternalProviders>.Instance);

    /// <summary>An id token as it comes from the token endpoint — the signature is TLS's job there.</summary>
    private static string IdToken(Dictionary<string, object> claims)
    {
        static string Part(object value) => Base64UrlEncoder.Encode(JsonSerializer.Serialize(value));
        return $"{Part(new { alg = "RS256", typ = "JWT" })}.{Part(claims)}.c2lnbmF0dXJl";
    }

    private static Dictionary<string, object> Google(string nonce = "n-1") => new()
    {
        ["iss"] = "https://accounts.google.com",
        ["aud"] = "google-client",
        ["sub"] = "1098765",
        ["exp"] = Now.AddMinutes(10).ToUnixTimeSeconds(),
        ["nonce"] = nonce,
        ["email"] = "anna@gmail.com",
        ["email_verified"] = true,
        ["given_name"] = "Anna",
        ["family_name"] = "Beispiel"
    };

    [Fact]
    public void A_google_token_that_holds_names_the_person()
    {
        var person = Providers().Read(ExternalProvider.Google, IdToken(Google()), "n-1");

        person.Should().NotBeNull();
        person!.Subject.Should().Be("1098765");
        person.EmailProven.Should().BeTrue();
        person.DisplayName.Should().Be("Anna Beispiel");
    }

    [Theory]
    [InlineData("nonce", "n-other")]
    [InlineData("aud", "another-client")]
    [InlineData("iss", "https://evil.example")]
    [InlineData("sub", "")]
    public void A_token_that_does_not_hold_signs_nobody_in(string claim, string value)
    {
        var claims = Google();
        claims[claim] = value;

        Providers().Read(ExternalProvider.Google, IdToken(claims), "n-1").Should().BeNull();
    }

    [Fact]
    public void An_expired_token_signs_nobody_in()
    {
        var claims = Google();
        claims["exp"] = Now.AddMinutes(-5).ToUnixTimeSeconds();

        Providers().Read(ExternalProvider.Google, IdToken(claims), "n-1").Should().BeNull();
    }

    [Fact]
    public void Google_may_name_its_issuer_without_the_scheme()
    {
        var claims = Google();
        claims["iss"] = "accounts.google.com";

        Providers().Read(ExternalProvider.Google, IdToken(claims), "n-1").Should().NotBeNull();
    }

    /// <summary>Microsoft's issuer carries the tenant, and its address is never proven.</summary>
    [Fact]
    public void A_microsoft_token_is_read_with_its_tenant_and_its_address_stays_unproven()
    {
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0",
            ["tid"] = "9188040d-6c67-4c5b-b112-36a304b66dad",
            ["aud"] = "ms-client",
            ["sub"] = "AAAAAAAAAAAAAAAAAAAAAIkzqFVrSaSaFHy782bbtaQ",
            ["exp"] = Now.AddMinutes(10).ToUnixTimeSeconds(),
            ["nonce"] = "n-1",
            ["email"] = "ceo@victim.example",
            ["email_verified"] = true,
            ["name"] = "Somebody"
        };

        var person = Providers().Read(ExternalProvider.Microsoft, IdToken(claims), "n-1");
        person!.EmailProven.Should().BeFalse("a tenant enters whatever address it likes");

        claims["tid"] = "another-tenant";
        Providers().Read(ExternalProvider.Microsoft, IdToken(claims), "n-1")
            .Should().BeNull("the issuer must be the token's own tenant");
    }

    [Fact]
    public void Apple_writes_email_verified_as_text()
    {
        var claims = new Dictionary<string, object>
        {
            ["iss"] = "https://appleid.apple.com",
            ["aud"] = "com.example.web",
            ["sub"] = "001234.abcdef",
            ["exp"] = Now.AddMinutes(10).ToUnixTimeSeconds(),
            ["nonce"] = "n-1",
            ["email"] = "x7y8@privaterelay.appleid.com",
            ["email_verified"] = "true",
            ["is_private_email"] = "true"
        };

        Providers().Read(ExternalProvider.Apple, IdToken(claims), "n-1")!.EmailProven.Should().BeTrue();
    }

    [Fact]
    public void The_redirect_carries_pkce_and_apple_asks_for_a_form_post()
    {
        var google = QueryHelpers.ParseQuery(Providers().AuthorizationUrl(ExternalProvider.Google, "s", "c", "n").Query);
        google["code_challenge_method"].ToString().Should().Be("S256");
        google["redirect_uri"].ToString().Should().Be("http://localhost:8090/auth/external/google/callback");
        google["scope"].ToString().Should().Be("openid email profile");
        google["prompt"].ToString().Should().Be("select_account");

        var apple = QueryHelpers.ParseQuery(Providers().AuthorizationUrl(ExternalProvider.Apple, "s", "c", "n").Query);
        apple["response_mode"].ToString().Should().Be("form_post");
        apple["scope"].ToString().Should().Be("openid email name");
    }

    [Fact]
    public void A_provider_without_its_credentials_has_no_button()
    {
        var settings = Settings();
        settings.Microsoft.ClientSecret = string.Empty;
        settings.Apple.PrivateKey = string.Empty;

        Providers(settings: settings).Configured.Should().Equal(ExternalProvider.Google);
        Providers(settings: new ExternalLoginSettings()).Configured.Should().BeEmpty();
    }

    [Fact]
    public async Task The_exchange_sends_the_verifier_and_reads_the_token()
    {
        var wire = new Wire(HttpStatusCode.OK, JsonSerializer.Serialize(new { id_token = IdToken(Google()), access_token = "at" }));

        var person = await Providers(wire).ExchangeAsync(ExternalProvider.Google, "the-code", "the-verifier", "n-1");

        person!.Subject.Should().Be("1098765");
        wire.Form!["code_verifier"].Should().Be("the-verifier");
        wire.Form["client_secret"].Should().Be("google-secret");
        wire.Address!.ToString().Should().Be("https://oauth2.googleapis.com/token");
    }

    [Fact]
    public async Task A_refused_code_is_no_person_and_a_silent_provider_is_no_refusal()
    {
        (await Providers(new Wire(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""))
            .ExchangeAsync(ExternalProvider.Google, "c", "v", "n")).Should().BeNull();

        var silent = () => Providers(new Wire(HttpStatusCode.ServiceUnavailable, "{}"))
            .ExchangeAsync(ExternalProvider.Google, "c", "v", "n");
        await silent.Should().ThrowAsync<ExternalProviderSilent>();

        var unreachable = () => Providers(new Wire(throws: true)).ExchangeAsync(ExternalProvider.Google, "c", "v", "n");
        await unreachable.Should().ThrowAsync<ExternalProviderSilent>();
    }

    [Fact]
    public void Apples_client_secret_is_a_short_jwt_the_team_signs()
    {
        var settings = Settings();
        var secret = Providers().AppleClientSecret(settings.Apple);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(secret);
        token.Alg.Should().Be("ES256");
        token.Kid.Should().Be("KEY1234567");
        token.Issuer.Should().Be("TEAM123456");
        token.Subject.Should().Be("com.example.web");
        token.Audiences.Should().Equal("https://appleid.apple.com");
        (token.ValidTo - token.IssuedAt).Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(5));

        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(AppleKey.Pkcs8), out _);
        var parts = secret.Split('.');
        key.VerifyData(
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Base64UrlEncoder.DecodeBytes(parts[2]), HashAlgorithmName.SHA256)
            .Should().BeTrue("signed with the team's key");
    }

    [Fact]
    public void The_challenge_is_the_s256_of_the_verifier()
    {
        // RFC 7636, appendix B.
        HttpExternalProviders.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
            .Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    private static class AppleKey
    {
        public static readonly string Pkcs8 = Create();

        private static string Create()
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return Convert.ToBase64String(key.ExportPkcs8PrivateKey());
        }
    }

    private sealed class Wire(HttpStatusCode status = HttpStatusCode.OK, string body = "{}", bool throws = false) : HttpMessageHandler
    {
        public Uri? Address { get; private set; }

        public Dictionary<string, string>? Form { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (throws)
            {
                throw new HttpRequestException("no route");
            }

            Address = request.RequestUri;
            Form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken))
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
