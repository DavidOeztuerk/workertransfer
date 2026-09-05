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

    /// <summary>Womit dieser Dienst sich bei GitHub meldet.</summary>
    /// <remarks>
    /// <strong>Pflicht, nicht Höflichkeit.</strong> GitHubs API beantwortet
    /// JEDE Anfrage ohne <c>User-Agent</c> mit <c>403</c> — nicht mit 400, nicht
    /// mit einer Meldung, die das Wort nennt. Gemessen am 04.09.2026: dieselbe
    /// Adresse antwortet mit Kopf <c>200</c> und ohne ihn <c>403</c>.
    /// <para>
    /// Solange er fehlte, hat dieser Dienst NIE funktioniert: der Gist-Nachweis
    /// nicht und der Abruf der Repositories auch nicht. Beides endete in
    /// „github unavailable", und weil das genau wie ein Ausfall bei GitHub
    /// aussieht, hat es niemand als eigenen Fehler gelesen.
    /// </para>
    /// </remarks>
    public string Benutzerkennung { get; set; } = "WorkerTransfer";

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
    /// <summary>Was der Besitzer selbst an das Repository geschrieben hat.</summary>
    /// <remarks>
    /// Kommt seit 2022 ohne Zutun in der Liste mit — kein zweiter Aufruf, kein
    /// besonderer Kopf. Und es ist der stärkste Beleg von allen, weil er eine
    /// NENNUNG ist: „kubernetes" hat ein Mensch dorthin geschrieben.
    /// </remarks>
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics,
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

    /// <summary>
    /// Wie viele Repositories höchstens einzeln nach ihren Sprachen gefragt
    /// werden.
    /// </summary>
    /// <remarks>
    /// Ein Aufruf JE Repository — bei hundert wären das hundert.
    /// <para>
    /// <strong>Ohne Token unterblieb das früher ganz, und das war falsch.</strong>
    /// Gemessen am 05.09.2026: GitHub meldet für <c>workertransfer</c> sieben
    /// Sprachen, die Seite zeigte eine — nämlich die Hauptsprache aus der
    /// Liste. Der Grund war das Ratenlimit, aber der Preis war eine Ansicht,
    /// die schmaler aussah als die Wahrheit, ohne es zu sagen (ADR-0022 §3).
    /// Sechzig Anfragen in der Stunde reichen sehr wohl für zehn Repositories
    /// je Abruf — das sind elf Anfragen und damit fünf Abrufe in der Stunde.
    /// </para>
    /// <para>
    /// Mit Token sind fünftausend erlaubt; dreissig sind dann ein Abruf, der in
    /// Sekunden fertig ist statt in einer Minute.
    /// </para>
    /// </remarks>
    private const int SprachenMitToken = 30;

    /// <summary>Dasselbe ohne Token — knapper, aber nicht null.</summary>
    private const int SprachenOhneToken = 10;

    /// <inheritdoc />
    public async Task<Abzug> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default)
    {
        var eintraege = await HoleAsync<List<RepoEintrag>>(
            $"/users/{Uri.EscapeDataString(login)}/repos"
            + $"?per_page={JeSeite}&sort=pushed&type=owner",
            cancellationToken);

        if (eintraege is null)
        {
            return new Abzug([], true);
        }

        // Forks bleiben draußen: eine Kopie fremder Arbeit ist kein Beleg für
        // eigene, und sie unter „meine Repositories" zu zeigen wäre genau die
        // stillschweigende Behauptung, die ADR-0022 ausschließt.
        var eigene = eintraege.Where(eintrag => !eintrag.Fork).ToList();

        // Die Liste kommt nach `pushed` sortiert — wenn das Budget nicht für
        // alle reicht, bekommen die zuletzt bearbeiteten ihre Sprachen.
        var budget = _einstellungen.Token.Length == 0
            ? SprachenOhneToken
            : SprachenMitToken;

        var belege = new List<Repository>(eigene.Count);

        foreach (var eintrag in eigene)
        {
            var name = eintrag.Name ?? string.Empty;

            belege.Add(new Repository(
                name,
                eintrag.Description ?? string.Empty,
                eintrag.Language,
                eintrag.StargazersCount,
                eintrag.HtmlUrl ?? string.Empty,
                eintrag.PushedAt,
                belege.Count < budget
                    ? await SprachenAsync(login, name, cancellationToken)
                    : [],
                eintrag.Topics ?? []));
        }

        return new Abzug(belege, eigene.Count <= budget);
    }

    /// <summary>Welche Sprachen in einem Repository vorkommen.</summary>
    /// <remarks>
    /// <strong>Nur die Namen, nie die Bytes.</strong> GitHub antwortet mit
    /// <c>{"Go": 12345, "Shell": 210}</c>; genau aus diesen Zahlen rechnete das
    /// gelöschte Paket sein „Können" als <c>bytes / total_bytes</c> — „eine
    /// eingecheckte Abhängigkeit schlägt jede sorgfältige Bibliothek" (ADR-0022
    /// §2). Sie hier gar nicht erst mitzunehmen ist billiger, als sie später zu
    /// verteidigen.
    /// <para>
    /// Ohne Token wird trotzdem gefragt, nur für weniger Repositories — wie
    /// viele, steht bei <see cref="SprachenOhneToken" />. Was übrig bleibt,
    /// meldet der <see cref="Abzug" /> als unvollständig, damit die Oberfläche
    /// es sagen kann.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> SprachenAsync(
        string login, string repository, CancellationToken cancellationToken)
    {
        if (repository.Length == 0)
        {
            return [];
        }

        var gemeldet = await HoleAsync<Dictionary<string, long>>(
            $"/repos/{Uri.EscapeDataString(login)}/{Uri.EscapeDataString(repository)}/languages",
            cancellationToken);

        return gemeldet is null ? [] : [.. gemeldet.Keys];
    }

    private async Task<T?> HoleAsync<T>(string pfad, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), pfad));

        anfrage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Ohne diesen Kopf antwortet GitHub auf alles mit 403. Siehe
        // `GitHubeinstellungen.Benutzerkennung`.
        anfrage.Headers.UserAgent.ParseAdd(_einstellungen.Benutzerkennung);

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
