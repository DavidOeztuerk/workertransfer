using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Network;

/// <summary>The credentials for GitHub's sign-in.</summary>
/// <remarks>
/// <strong>Empty means off</strong>, and then the gist stays the path. The same
/// stance as with the drafting help: a default that contains a secret IS the
/// secret — and it would then sit in version control.
/// <para>
/// <c>Kennung</c>, <c>Geheimnis</c>, <c>Rueckadresse</c> and
/// <c>Zeitueberschreitung</c> ARE the environment keys
/// (<c>GitHub__OAuth__Kennung</c> and so on), so they stay German until the keys
/// are renamed everywhere at once — in compose, in the chart and in
/// <c>.env.example</c>. <c>Rueckadresse</c> had been renamed to
/// <c>RedirectUrl</c> on its own: compose still set
/// <c>GitHub__OAuth__Rueckadresse</c>, the property stayed empty, and the
/// sign-in counted as not configured.
/// </para>
/// </remarks>
public sealed class GitHubSignInSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "GitHub:OAuth";

    /// <summary>The OAuth application's id. Empty means: there is none.</summary>
    public string Kennung { get; set; } = string.Empty;

    /// <summary>The secret that goes with it.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Where GitHub sends the browser back to.</summary>
    public string Rueckadresse { get; set; } = string.Empty;

    /// <summary>Where GitHub's sign-in lies. Switchable for probes.</summary>
    public string AuthorizeUrl { get; set; } = "https://github.com";

    /// <summary>How long an exchange may take.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>What GitHub answers to the exchange.</summary>
internal sealed record ExchangeResponse(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>What <c>GET /user</c> yields of what is needed here.</summary>
internal sealed record AccountEntry(
    [property: JsonPropertyName("login")] string? Login);

/// <inheritdoc cref="IGitHubSignIn" />
public sealed class HttpGitHubSignIn(
    IHttpClientFactory factory,
    IOptions<GitHubSignInSettings> signIn,
    IOptions<GitHubSettings> api) : IGitHubSignIn
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "github-oauth";

    private readonly GitHubSignInSettings _signIn = signIn.Value;
    private readonly GitHubSettings _api = api.Value;

    /// <inheritdoc />
    public bool IsConfigured =>
        _signIn.Kennung.Length > 0
        && _signIn.Geheimnis.Length > 0
        && _signIn.Rueckadresse.Length > 0;

    /// <inheritdoc />
    public Uri AuthorizeUrl(string state)
    {
        // WITHOUT `scope`. `GET /user` answers the question about one's own
        // name without any right; asking for a `repo` right in order to read
        // public repositories would be a power of attorney for something that
        // is public anyway.
        var query =
            $"client_id={Uri.EscapeDataString(_signIn.Kennung)}"
            + $"&redirect_uri={Uri.EscapeDataString(_signIn.Rueckadresse)}"
            + $"&state={Uri.EscapeDataString(state)}"
            + "&scope=";

        return new Uri(
            new Uri(_signIn.AuthorizeUrl), $"/login/oauth/authorize?{query}");
    }

    /// <inheritdoc />
    public async Task<string?> LoginNameAsync(
        string code, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var token = await ExchangeAsync(code, cancellationToken);

        return token is null ? null : await NamesAsync(token, cancellationToken);
    }

    /// <summary>The one-time code is exchanged for an access token.</summary>
    private async Task<string?> ExchangeAsync(string code, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(ClientName);
        client.Timeout = _signIn.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_signIn.AuthorizeUrl), "/login/oauth/access_token"))
        {
            Content = JsonContent.Create(new
            {
                client_id = _signIn.Kennung,
                client_secret = _signIn.Geheimnis,
                code,
                redirect_uri = _signIn.Rueckadresse
            })
        };

        // Without this header GitHub answers in its old form encoding instead
        // of in JSON — and the reader below would find nothing.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_api.UserAgent);

        var response = await SendAsync(client, request, cancellationToken);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubSilent($"github returned {(int)response.StatusCode}");
            }

            var read = JsonSerializer.Deserialize<ExchangeResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken));

            // A refused code is a statement about the code, not an outage —
            // GitHub answers it with 200 and an `error` field.
            return read?.AccessToken is { Length: > 0 } token ? token : null;
        }
    }

    /// <summary>Ask once with the token whom it belongs to — and forget it.</summary>
    private async Task<string?> NamesAsync(string token, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(ClientName);
        client.Timeout = _signIn.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_api.Adresse), "/user"));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd(_api.UserAgent);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await SendAsync(client, request, cancellationToken);

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubSilent($"github returned {(int)response.StatusCode}");
            }

            var account = JsonSerializer.Deserialize<AccountEntry>(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return account?.Login is { Length: > 0 } name ? name : null;
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // The kind, never the content.
            throw new GitHubSilent($"github is unreachable ({error.GetType().Name})", error);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubSilent("github did not answer in time");
        }
    }
}
