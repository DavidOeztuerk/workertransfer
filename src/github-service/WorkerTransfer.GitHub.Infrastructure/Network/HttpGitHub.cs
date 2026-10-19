using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Infrastructure.Network;

/// <summary>Where to ask, and with what.</summary>
/// <remarks>
/// <c>BaseUrl</c> and <c>Timeout</c> ARE the environment keys
/// (<c>GitHub__BaseUrl</c>, <c>GitHub__Timeout</c>); renaming one alone leaves its
/// value on the default, silently.
/// </remarks>
public sealed class GitHubSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "GitHub";

    /// <summary>The base address of the API.</summary>
    public string BaseUrl { get; set; } = "https://api.github.com";

    /// <summary>Optional.</summary>
    /// <remarks>
    /// A token raises the rate limit from sixty to five thousand an hour.
    /// Without a token everything runs just the same, only leaner — which is
    /// why it is optional and not a condition.
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>What this service identifies itself to GitHub with.</summary>
    /// <remarks>
    /// <strong>Required, not politeness.</strong> GitHub's API answers EVERY
    /// request without a <c>User-Agent</c> with <c>403</c> — not with a 400, not
    /// with a message naming the header. Measured 04.09.2026: the same address
    /// answers <c>200</c> with the header and <c>403</c> without it.
    /// <para>
    /// For as long as it was missing, this service NEVER worked: neither the
    /// gist proof nor the repository fetch. Both ended in "github
    /// unavailable", and because that looks exactly like an outage at GitHub,
    /// nobody read it as a fault of our own.
    /// </para>
    /// </remarks>
    public string UserAgent { get; set; } = "WorkerTransfer";

    /// <summary>How long a fetch may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>What a gist entry yields of what is needed here.</summary>
internal sealed record GistEntry(
    [property: JsonPropertyName("description")] string? Description);

/// <summary>What a repository entry yields.</summary>
internal sealed record RepoEntry(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    /// <summary>What the owner themselves wrote onto the repository.</summary>
    /// <remarks>
    /// Has travelled along in the listing since 2022 without anything being done
    /// — no second call, no special header. And it is the strongest evidence of
    /// all, because it is a NAMING: a human being wrote "kubernetes" there.
    /// </remarks>
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics,
    [property: JsonPropertyName("fork")] bool Fork);

/// <summary>Asks GitHub — once, at a human being's request.</summary>
public sealed class HttpGitHub(
    IHttpClientFactory factory,
    IOptions<GitHubSettings> settings,
    ILogger<HttpGitHub> log) : IGitHub
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "github";

    /// <summary>The first page only.</summary>
    /// <remarks>
    /// Whoever has more than a hundred public repositories gets the hundred
    /// most recently changed — an honest selection, and the view says what it
    /// was sorted by. Fetching everything would mean burdening everybody else
    /// with several fetches for the rare cases.
    /// </remarks>
    public const int PerPage = 100;

    private readonly GitHubSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> HasProofGistAsync(
        string login, string nonce, CancellationToken cancellationToken = default)
    {
        var wanted = Connection.GistDescription(nonce);

        var gists = await GetAsync<List<GistEntry>>(
            $"/users/{Uri.EscapeDataString(login)}/gists?per_page={PerPage}", cancellationToken);

        return gists?.Any(entry =>
            string.Equals(entry.Description, wanted, StringComparison.Ordinal)) ?? false;
    }

    /// <summary>
    /// How many repositories are asked individually for their languages at most.
    /// </summary>
    /// <remarks>
    /// One call PER repository — with a hundred that would be a hundred.
    /// <para>
    /// <strong>Without a token this used to be skipped entirely, and that was
    /// wrong.</strong> Measured 05.09.2026: GitHub reports seven languages for
    /// <c>workertransfer</c>, the page showed one — namely the main language
    /// out of the listing. The reason was the rate limit, but the price was a
    /// view that looked narrower than the truth without saying so (ADR-0022
    /// §3). Sixty requests an hour are perfectly enough for ten repositories
    /// per fetch — that is eleven requests and therefore five fetches an hour.
    /// </para>
    /// <para>
    /// With a token five thousand are allowed; thirty are then one fetch that
    /// finishes in seconds instead of in a minute.
    /// </para>
    /// </remarks>
    private const int LanguagesWithToken = 30;

    /// <summary>The same without a token — leaner, but not zero.</summary>
    private const int LanguagesWithoutToken = 10;

    /// <inheritdoc />
    public async Task<Snapshot> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default)
    {
        var entries = await GetAsync<List<RepoEntry>>(
            $"/users/{Uri.EscapeDataString(login)}/repos"
            + $"?per_page={PerPage}&sort=pushed&type=owner",
            cancellationToken);

        if (entries is null)
        {
            return new Snapshot([], true);
        }

        // Forks stay out: a copy of somebody else's work is no evidence of
        // one's own, and showing it under "my repositories" would be exactly
        // the silent assertion ADR-0022 excludes.
        var own = entries.Where(entry => !entry.Fork).ToList();

        // The listing arrives sorted by `pushed` — when the budget does not
        // stretch to all of them, the most recently worked on get their
        // languages.
        var budget = _settings.Token.Length == 0
            ? LanguagesWithoutToken
            : LanguagesWithToken;

        var evidences = new List<Repository>(own.Count);

        foreach (var entry in own)
        {
            var name = entry.Name ?? string.Empty;

            evidences.Add(new Repository(
                name,
                entry.Description ?? string.Empty,
                entry.Language,
                entry.StargazersCount,
                entry.HtmlUrl ?? string.Empty,
                entry.PushedAt,
                evidences.Count < budget
                    ? await LanguagesAsync(login, name, cancellationToken)
                    : [],
                entry.Topics ?? []));
        }

        return new Snapshot(evidences, own.Count <= budget);
    }

    /// <summary>Which languages occur in a repository.</summary>
    /// <remarks>
    /// <strong>The names only, never the bytes.</strong> GitHub answers with
    /// <c>{"Go": 12345, "Shell": 210}</c>; out of exactly those numbers the
    /// deleted package computed its "skill" as <c>bytes / total_bytes</c> — "a
    /// checked-in dependency beats every careful library" (ADR-0022 §2). Not
    /// taking them along in the first place is cheaper than defending them
    /// later.
    /// <para>
    /// Without a token the question is still asked, only for fewer repositories
    /// — how many stands at <see cref="LanguagesWithoutToken" />. What is left
    /// over the <see cref="Snapshot" /> reports as incomplete, so the interface
    /// can say so.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> LanguagesAsync(
        string login, string repository, CancellationToken cancellationToken)
    {
        if (repository.Length == 0)
        {
            return [];
        }

        var reported = await GetAsync<Dictionary<string, long>>(
            $"/repos/{Uri.EscapeDataString(login)}/{Uri.EscapeDataString(repository)}/languages",
            cancellationToken);

        return reported is null ? [] : [.. reported.Keys];
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), path));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Without this header GitHub answers everything with 403. See
        // `GitHubSettings.UserAgent`.
        request.Headers.UserAgent.ParseAdd(_settings.UserAgent);

        if (_settings.Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("GitHub unreachable ({Kind})", error.GetType().Name);
            throw new GitHubSilent("github unreachable", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("GitHub did not answer in time");
            throw new GitHubSilent("github did not answer in time", error);
        }

        using (response)
        {
            // "Does not exist" is an answer, not an outage.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("GitHub answered with {Code}", (int)response.StatusCode);
                throw new GitHubSilent($"github returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException error)
            {
                throw new GitHubSilent("github sent an unusable answer", error);
            }
        }
    }
}
