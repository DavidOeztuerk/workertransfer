using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Infrastructure.Netz;

/// <summary>Wohin gefragt wird, und womit.</summary>
public sealed class GitHubeinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "GitHub";

    /// <summary>Die Basisadresse der API.</summary>
    public string Adresse { get; set; } = "https://api.github.com";

    /// <summary>Optional.</summary>
    /// <remarks>
    /// Ein Token hebt das Ratenlimit von sechzig auf fünftausend je Stunde.
    /// Ohne Token läuft alles genauso, nur knapper — deshalb ist es optional
    /// und nicht Bedingung.
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>Wie lange ein Abruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Was ein Gist-Eintrag davon hergibt, was hier gebraucht wird.</summary>
internal sealed record GistEintrag(
    [property: JsonPropertyName("description")] string? Description);

/// <summary>Was ein Repository-Eintrag hergibt.</summary>
internal sealed record RepoEintrag(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stargazers_count")] int StargazersCount,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    [property: JsonPropertyName("fork")] bool Fork);

/// <summary>Fragt GitHub — einmal, auf Bitte eines Menschen.</summary>
public sealed class HttpGitHub(
    IHttpClientFactory fabrik,
    IOptions<GitHubeinstellungen> einstellungen,
    ILogger<HttpGitHub> protokoll) : IGitHub
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "github";

    /// <summary>Nur die erste Seite.</summary>
    /// <remarks>
    /// Wer mehr als hundert öffentliche Repositories hat, bekommt die hundert
    /// zuletzt geänderten — eine ehrliche Auswahl, und die Anzeige sagt, wonach
    /// sortiert wurde. Alles zu holen hieße, für die seltenen Fälle jedem
    /// anderen mehrere Abrufe aufzubürden.
    /// </remarks>
    public const int JeSeite = 100;

    private readonly GitHubeinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> HatNachweisgistAsync(
        string login, string einmalzeichenfolge, CancellationToken cancellationToken = default)
    {
        var gesucht = Verbindung.Gistbeschreibung(einmalzeichenfolge);

        var gists = await HoleAsync<List<GistEintrag>>(
            $"/users/{Uri.EscapeDataString(login)}/gists?per_page={JeSeite}", cancellationToken);

        return gists?.Any(eintrag =>
            string.Equals(eintrag.Description, gesucht, StringComparison.Ordinal)) ?? false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Repository>> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default)
    {
        var eintraege = await HoleAsync<List<RepoEintrag>>(
            $"/users/{Uri.EscapeDataString(login)}/repos"
            + $"?per_page={JeSeite}&sort=pushed&type=owner",
            cancellationToken);

        if (eintraege is null)
        {
            return [];
        }

        return
        [
            .. eintraege
                // Forks bleiben draußen: eine Kopie fremder Arbeit ist kein
                // Beleg für eigene, und sie unter „meine Repositories" zu
                // zeigen wäre genau die stillschweigende Behauptung, die
                // ADR-0022 ausschließt.
                .Where(eintrag => !eintrag.Fork)
                .Select(eintrag => new Repository(
                    eintrag.Name ?? string.Empty,
                    eintrag.Description ?? string.Empty,
                    eintrag.Language,
                    eintrag.StargazersCount,
                    eintrag.HtmlUrl ?? string.Empty,
                    eintrag.PushedAt))
        ];
    }

    private async Task<T?> HoleAsync<T>(string pfad, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), pfad));

        anfrage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (_einstellungen.Token is { Length: > 0 } token)
        {
            anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning("GitHub nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new GitHubSchweigt("github unreachable", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("GitHub antwortete nicht rechtzeitig");
            throw new GitHubSchweigt("github did not answer in time", fehler);
        }

        using (antwort)
        {
            // „Gibt es nicht" ist eine Antwort, kein Ausfall.
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!antwort.IsSuccessStatusCode)
            {
                protokoll.LogWarning("GitHub antwortete mit {Code}", (int)antwort.StatusCode);
                throw new GitHubSchweigt($"github returned {(int)antwort.StatusCode}");
            }

            try
            {
                return await antwort.Content.ReadFromJsonAsync<T>(cancellationToken);
            }
            catch (JsonException fehler)
            {
                throw new GitHubSchweigt("github sent an unusable answer", fehler);
            }
        }
    }
}
