using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Netz;

/// <summary>Die Zugangsdaten für GitHubs Anmeldung.</summary>
/// <remarks>
/// <strong>Leer heisst aus</strong>, und dann bleibt der Gist der Weg. Dieselbe
/// Haltung wie bei der Formulierungshilfe: eine Voreinstellung, die ein
/// Geheimnis enthält, IST das Geheimnis — und es läge dann in der
/// Versionsverwaltung.
/// </remarks>
public sealed class GitHubAnmeldeeinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "GitHub:OAuth";

    /// <summary>Die Kennung der OAuth-Anwendung. Leer heisst: gibt es nicht.</summary>
    public string Kennung { get; set; } = string.Empty;

    /// <summary>Das Geheimnis dazu.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Wohin GitHub den Browser zurückschickt.</summary>
    public string Rueckadresse { get; set; } = string.Empty;

    /// <summary>Wo GitHubs Anmeldung liegt. Für Proben umstellbar.</summary>
    public string Anmeldeadresse { get; set; } = "https://github.com";

    /// <summary>Wie lange ein Tausch dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Was GitHub auf den Tausch antwortet.</summary>
internal sealed record Tauschantwort(
    [property: JsonPropertyName("access_token")] string? AccessToken,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>Was <c>GET /user</c> davon hergibt, was hier gebraucht wird.</summary>
internal sealed record Kontoeintrag(
    [property: JsonPropertyName("login")] string? Login);

/// <inheritdoc cref="IGitHubAnmeldung" />
public sealed class HttpGitHubAnmeldung(
    IHttpClientFactory fabrik,
    IOptions<GitHubAnmeldeeinstellungen> anmeldung,
    IOptions<GitHubeinstellungen> api) : IGitHubAnmeldung
{
    /// <summary>Der Name, unter dem der Client registriert ist.</summary>
    public const string Klient = "github-oauth";

    private readonly GitHubAnmeldeeinstellungen _anmeldung = anmeldung.Value;
    private readonly GitHubeinstellungen _api = api.Value;

    /// <inheritdoc />
    public bool Eingerichtet =>
        _anmeldung.Kennung.Length > 0
        && _anmeldung.Geheimnis.Length > 0
        && _anmeldung.Rueckadresse.Length > 0;

    /// <inheritdoc />
    public Uri Anmeldeadresse(string zustand)
    {
        // OHNE `scope`. `GET /user` beantwortet die Frage nach dem eigenen
        // Namen ohne jedes Recht; ein `repo`-Recht zu erbitten, um öffentliche
        // Repositories zu lesen, wäre eine Vollmacht für etwas, das ohnehin
        // öffentlich ist.
        var abfrage =
            $"client_id={Uri.EscapeDataString(_anmeldung.Kennung)}"
            + $"&redirect_uri={Uri.EscapeDataString(_anmeldung.Rueckadresse)}"
            + $"&state={Uri.EscapeDataString(zustand)}"
            + "&scope=";

        return new Uri(
            new Uri(_anmeldung.Anmeldeadresse), $"/login/oauth/authorize?{abfrage}");
    }

    /// <inheritdoc />
    public async Task<string?> AnmeldenamenAsync(
        string code, CancellationToken cancellationToken = default)
    {
        if (!Eingerichtet)
        {
            return null;
        }

        var token = await TauscheAsync(code, cancellationToken);

        return token is null ? null : await NamenAsync(token, cancellationToken);
    }

    /// <summary>Der Einmalcode wird gegen ein Zugriffstoken getauscht.</summary>
    private async Task<string?> TauscheAsync(string code, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _anmeldung.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_anmeldung.Anmeldeadresse), "/login/oauth/access_token"))
        {
            Content = JsonContent.Create(new
            {
                client_id = _anmeldung.Kennung,
                client_secret = _anmeldung.Geheimnis,
                code,
                redirect_uri = _anmeldung.Rueckadresse
            })
        };

        // Ohne diesen Kopf antwortet GitHub in seiner alten Formularschreibweise
        // statt in JSON — und der Leser darunter fände nichts.
        anfrage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        anfrage.Headers.UserAgent.ParseAdd(_api.Benutzerkennung);

        var antwort = await SendeAsync(client, anfrage, cancellationToken);

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new GitHubSchweigt($"github returned {(int)antwort.StatusCode}");
            }

            var gelesen = JsonSerializer.Deserialize<Tauschantwort>(
                await antwort.Content.ReadAsStringAsync(cancellationToken));

            // Ein abgelehnter Code ist eine Aussage über den Code, kein Ausfall
            // — GitHub antwortet darauf mit 200 und einem `error`-Feld.
            return gelesen?.AccessToken is { Length: > 0 } token ? token : null;
        }
    }

    /// <summary>Mit dem Token einmal fragen, wem es gehört — und es vergessen.</summary>
    private async Task<string?> NamenAsync(string token, CancellationToken cancellationToken)
    {
        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _anmeldung.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_api.Adresse), "/user"));

        anfrage.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        anfrage.Headers.UserAgent.ParseAdd(_api.Benutzerkennung);
        anfrage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var antwort = await SendeAsync(client, anfrage, cancellationToken);

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new GitHubSchweigt($"github returned {(int)antwort.StatusCode}");
            }

            var konto = JsonSerializer.Deserialize<Kontoeintrag>(
                await antwort.Content.ReadAsStringAsync(cancellationToken));

            return konto?.Login is { Length: > 0 } name ? name : null;
        }
    }

    private static async Task<HttpResponseMessage> SendeAsync(
        HttpClient client, HttpRequestMessage anfrage, CancellationToken cancellationToken)
    {
        try
        {
            return await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            // Die Art, nie der Inhalt.
            throw new GitHubSchweigt($"github is unreachable ({fehler.GetType().Name})", fehler);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubSchweigt("github did not answer in time");
        }
    }
}
