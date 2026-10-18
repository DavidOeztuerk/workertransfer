using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Where the providers answer, and with which credentials (ADR-0053).</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>ExternalLogin__CallbackBase</c>,
/// <c>ExternalLogin__Google__ClientId</c>, …). A provider without a client id
/// is off, and the page shows no button for it. The endpoints stand in compose
/// and the chart, measured on 18.10.2026 from each discovery document — the
/// egress boundary lets through what the configuration names.
/// </remarks>
public sealed class ExternalLoginSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "ExternalLogin";

    /// <summary>The gateway's public address — the callback is <c>{CallbackBase}/auth/external/{provider}/callback</c>.</summary>
    public string CallbackBase { get; set; } = string.Empty;

    /// <summary>How long one exchange may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Google.</summary>
    public ExternalProviderSettings Google { get; set; } = new();

    /// <summary>Microsoft.</summary>
    public ExternalProviderSettings Microsoft { get; set; } = new();

    /// <summary>Apple — its client secret is a JWT signed with the team's key.</summary>
    public ExternalProviderSettings Apple { get; set; } = new();

    /// <summary>The settings of one provider.</summary>
    public ExternalProviderSettings For(ExternalProvider provider) => provider switch
    {
        ExternalProvider.Google => Google,
        ExternalProvider.Microsoft => Microsoft,
        ExternalProvider.Apple => Apple,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}

/// <summary>One provider's client and endpoints.</summary>
public sealed class ExternalProviderSettings
{
    /// <summary>The client id registered at the provider; empty means off.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret (Google, Microsoft).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Apple: the team id that signs the client secret.</summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>Apple: the id of the signing key.</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Apple: the signing key, PKCS#8, base64 (the body of the <c>.p8</c> file).</summary>
    public string PrivateKey { get; set; } = string.Empty;

    /// <summary>Where the browser signs in.</summary>
    public string AuthorizationEndpoint { get; set; } = string.Empty;

    /// <summary>Where the code is exchanged.</summary>
    public string TokenEndpoint { get; set; } = string.Empty;

    /// <summary>The token's issuer; Microsoft's carries <c>{tenantid}</c>.</summary>
    public string Issuer { get; set; } = string.Empty;
}

/// <summary>Google, Microsoft and Apple over OpenID Connect — code, PKCE, nonce (ADR-0053).</summary>
/// <remarks>
/// <para><strong>The id token's signature is not checked, its content is.</strong>
/// The token comes straight from the provider's token endpoint over TLS in
/// exchange for our code and secret — OpenID Connect Core §3.1.3.7 lets TLS
/// stand in for the signature there. Issuer, audience, expiry and nonce are
/// checked every time; a token that fails any of them signs nobody in.</para>
///
/// <para>Nothing of the provider's answer is logged and no token is kept.</para>
/// </remarks>
public sealed class HttpExternalProviders(
    IHttpClientFactory factory,
    IOptions<ExternalLoginSettings> settings,
    TimeProvider clock,
    ILogger<HttpExternalProviders> log) : IExternalProviders
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "external-login";

    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

    private readonly ExternalLoginSettings _settings = settings.Value;

    /// <inheritdoc />
    public IReadOnlyList<ExternalProvider> Configured =>
        [.. ExternalProviders.All.Where(IsConfigured)];

    /// <inheritdoc />
    public Uri AuthorizationUrl(ExternalProvider provider, string state, string codeChallenge, string nonce)
    {
        var options = _settings.For(provider);
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = Callback(provider),
            ["response_type"] = "code",
            ["scope"] = provider == ExternalProvider.Apple ? "openid email name" : "openid email profile",
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256"
        };

        if (provider == ExternalProvider.Apple)
        {
            // Apple answers a request for name or email only as a form post.
            query["response_mode"] = "form_post";
        }
        else
        {
            // The person chooses the account, instead of the browser's last one.
            query["prompt"] = "select_account";
        }

        var encoded = string.Join('&', query.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{options.AuthorizationEndpoint}?{encoded}");
    }

    /// <inheritdoc />
    public async Task<ExternalIdentity?> ExchangeAsync(
        ExternalProvider provider, string code, string codeVerifier, string nonce, CancellationToken cancellationToken = default)
    {
        var options = _settings.For(provider);

        if (!IsConfigured(provider))
        {
            return null;
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = Callback(provider),
            ["client_id"] = options.ClientId,
            ["client_secret"] = provider == ExternalProvider.Apple ? AppleClientSecret(options) : options.ClientSecret,
            ["code_verifier"] = codeVerifier
        });

        HttpResponseMessage response;

        try
        {
            response = await client.PostAsync(new Uri(options.TokenEndpoint), form, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new ExternalProviderSilent($"{ExternalProviders.Word(provider)} did not answer.", error);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new ExternalProviderSilent($"{ExternalProviders.Word(provider)} answered {(int)response.StatusCode}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                // invalid_grant and its siblings: a reused or expired code.
                log.LogWarning("A provider refused the code exchange ({Status})", (int)response.StatusCode);
                return null;
            }

            string? idToken;

            try
            {
                var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                idToken = body.TryGetProperty("id_token", out var value) ? value.GetString() : null;
            }
            catch (JsonException)
            {
                idToken = null;
            }

            return idToken is null ? null : Read(provider, idToken, nonce);
        }
    }

    /// <summary>The person, out of an id token whose issuer, audience, expiry and nonce hold — else <c>null</c>.</summary>
    public ExternalIdentity? Read(ExternalProvider provider, string idToken, string nonce)
    {
        var options = _settings.For(provider);
        JsonWebToken token;

        try
        {
            token = new JsonWebTokenHandler().ReadJsonWebToken(idToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var issuer = Claim(token, "iss");
        var expected = options.Issuer.Replace("{tenantid}", Claim(token, "tid") ?? "\u0000", StringComparison.Ordinal);
        var subject = Claim(token, "sub");

        if (issuer is null || !(issuer == expected
                                || (provider == ExternalProvider.Google && issuer == "accounts.google.com"))
            || !token.Audiences.Contains(options.ClientId, StringComparer.Ordinal)
            || token.ValidTo == DateTime.MinValue
            || token.ValidTo < clock.GetUtcNow().UtcDateTime - Skew
            || !string.Equals(Claim(token, "nonce"), nonce, StringComparison.Ordinal)
            || string.IsNullOrEmpty(subject))
        {
            log.LogWarning("A provider's id token did not hold ({Provider})", ExternalProviders.Word(provider));
            return null;
        }

        return new ExternalIdentity(
            provider,
            subject,
            Claim(token, "email"),
            // Google writes a boolean, Apple the string "true".
            string.Equals(Claim(token, "email_verified"), "true", StringComparison.OrdinalIgnoreCase),
            Claim(token, "given_name"),
            Claim(token, "family_name"),
            Claim(token, "name"));
    }

    private bool IsConfigured(ExternalProvider provider)
    {
        var options = _settings.For(provider);

        return _settings.CallbackBase.Length > 0
               && options.ClientId.Length > 0
               && options.AuthorizationEndpoint.Length > 0
               && options.TokenEndpoint.Length > 0
               && options.Issuer.Length > 0
               && (provider == ExternalProvider.Apple
                   ? options.TeamId.Length > 0 && options.KeyId.Length > 0 && options.PrivateKey.Length > 0
                   : options.ClientSecret.Length > 0);
    }

    private string Callback(ExternalProvider provider) =>
        $"{_settings.CallbackBase.TrimEnd('/')}/auth/external/{ExternalProviders.Word(provider)}/callback";

    private static string? Claim(JsonWebToken token, string type) =>
        token.TryGetClaim(type, out var claim) ? claim.Value : null;

    /// <summary>Apple's client secret: a short JWT the team signs with its key, made per exchange.</summary>
    public string AppleClientSecret(ExternalProviderSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Convert.FromBase64String(Pem(options.PrivateKey)), out _);
        var now = clock.GetUtcNow().UtcDateTime;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.TeamId,
            Audience = options.Issuer,
            Subject = new ClaimsIdentity([new Claim("sub", options.ClientId)]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(key) { KeyId = options.KeyId }, SecurityAlgorithms.EcdsaSha256)
        });
    }

    /// <summary>The base64 body of a key — with or without its PEM lines.</summary>
    private static string Pem(string key) =>
        string.Concat(key.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("-----", StringComparison.Ordinal)));

    /// <summary>The name in Apple's <c>user</c> form field — sent on the first authorisation only, never in the token.</summary>
    /// <remarks>Apple's wire, camelCase: <c>{"name":{"firstName":…,"lastName":…},"email":…}</c>.</remarks>
    public static (string? Given, string? Family)? AppleName(string? user)
    {
        if (string.IsNullOrEmpty(user))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(user);

            if (!document.RootElement.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            static string? Part(JsonElement name, string field) =>
                name.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            return (Part(name, "firstName"), Part(name, "lastName"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A fresh random value for state, verifier and nonce — 32 bytes, base64url.</summary>
    public static string Random() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>The PKCE challenge for a verifier.</summary>
    public static string Challenge(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
}
