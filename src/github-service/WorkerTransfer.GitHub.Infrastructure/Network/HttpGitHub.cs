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

/// <summary>Wohin gefragt wird, und womit.</summary>
public sealed class GitHubSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "GitHub";

    /// <summary>Die Basisadresse der API.</summary>
    public string Adresse { get; set; } = "https://api.github.com";

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
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>What a gist entry yields of what is needed here.</summary>
internal sealed record GistEintrag(
    [property: JsonPropertyName("description")] string? Description);

/// <summary>What a repository entry yields.</summary>
internal sealed record RepoEintrag(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    /// <summary>Was der Besitzer selbst an das Repository geschrieben hat.</summary>
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
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "github";

    /// <summary>The first page only.</summary>
    /// <remarks>
    /// Whoever has more than a hundred public repositories gets the hundred
    /// most recently changed — an honest selection, and the view says what it
    /// was sorted by. Fetching everything would mean burdening everybody else
    /// with several fetches for the rare cases.
    /// </remarks>
    public const int JeSeite = 100;

    private readonly GitHubSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> HasProofGistAsync(
        string login, string nonce, CancellationToken cancellationToken = default)
    {
        var gesucht = Connection.GistDescription(nonce);

        var gists = await GetAsync<List<GistEintrag>>(
            $"/users/{Uri.EscapeDataString(login)}/gists?per_page={JeSeite}", cancellationToken);

        return gists?.Any(entry =>
            string.Equals(entry.Description, gesucht, StringComparison.Ordinal)) ?? false;
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
        var entries = await GetAsync<List<RepoEintrag>>(
            $"/users/{Uri.EscapeDataString(login)}/repos"
            + $"?per_page={JeSeite}&sort=pushed&type=owner",
            cancellationToken);

        if (entries is null)
        {
            return new Snapshot([], true);
        }

        // Forks stay out: a copy of somebody else's work is no evidence of
        // one's own, and showing it under "my repositories" would be exactly
        // the silent assertion ADR-0022 excludes.
        var eigene = entries.Where(entry => !entry.Fork).ToList();

        // The listing arrives sorted by `pushed` — when the budget does not
        // stretch to all of them, the most recently worked on get their
        // languages.
        var budget = _settings.Token.Length == 0
            ? LanguagesWithoutToken
            : LanguagesWithToken;

        var evidences = new List<Repository>(eigene.Count);

        foreach (var entry in eigene)
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

        return new Snapshot(evidences, eigene.Count <= budget);
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

        var gemeldet = await GetAsync<Dictionary<string, long>>(
            $"/repos/{Uri.EscapeDataString(login)}/{Uri.EscapeDataString(repository)}/languages",
            cancellationToken);

        return gemeldet is null ? [] : [.. gemeldet.Keys];
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(Klient);
        client.Timeout = _settings.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.Adresse), path));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Ohne diesen Header antwortet GitHub auf alles mit 403. Siehe
        // `GitHubSettings.Benutzerkennung`.
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
            log.LogWarning("GitHub nicht erreichbar ({Art})", error.GetType().Name);
            throw new GitHubSilent("github unreachable", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("GitHub antwortete nicht rechtzeitig");
            throw new GitHubSilent("github did not answer in time", error);
        }

        using (response)
        {
            // „Gibt es nicht" ist eine Antwort, kein Ausfall.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("GitHub antwortete mit {Code}", (int)response.StatusCode);
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
