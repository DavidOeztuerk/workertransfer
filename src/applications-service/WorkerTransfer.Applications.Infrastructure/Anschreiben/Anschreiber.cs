using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Anschreiben;

/// <summary>Ob und wohin ein Modell gefragt wird.</summary>
/// <remarks>
/// Derselbe Abschnitt <c>Draft</c> wie in jobs-service und profile-service:
/// eine Plattform, ein Anbieter, ein Schluessel. Zwei Abschnitte waeren zwei
/// Gelegenheiten, einen davon zu vergessen.
/// </remarks>
public sealed class Anschreibeneinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Draft";

    /// <summary>Leer heißt: es wird nichts gerufen, und die Oberfläche sagt das.</summary>
    public string Schluessel { get; set; } = string.Empty;

    /// <summary>Die Adresse des Anbieters.</summary>
    public string Adresse { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Sechzig und nicht dreissig Sekunden wie beim Anzeigenentwurf: ein
    /// Anschreiben ist laenger und traegt mehr Kontext. Aber eben nicht
    /// unbegrenzt — ohne diese Zeile gilt die Vorgabe von <c>HttpClient</c> mit
    /// HUNDERT Sekunden.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Welches Modell.</summary>
    public string Modell { get; set; } = "claude-sonnet-5";
}

/// <summary>Der Anschreiber, wenn keiner eingerichtet ist.</summary>
/// <remarks>
/// Er liefert keine Vorlage, sondern sagt, dass es ihn nicht gibt. Ein
/// Anschreiben, das nicht vom Modell kommt, aber so aussieht, waere die
/// schlechtere Antwort — und im Stand „Pruefen" eine Einladung, es
/// versehentlich freizugeben (ADR-0034).
/// </remarks>
public sealed class KeinAnschreiber : IAnschreiber
{
    /// <inheritdoc />
    public Task<string> SchreibeAsync(
        KiZugang zugang,
        Anschreibenkontext kontext,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zugang);
        ArgumentNullException.ThrowIfNull(kontext);
        _ = fortschritt;
        _ = cancellationToken;

        return Task.FromException<string>(
            new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet."));
    }

    /// <inheritdoc />
    public Task<string> UeberarbeiteAsync(
        KiZugang zugang,
        Anschreibenkontext kontext,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zugang);
        ArgumentNullException.ThrowIfNull(kontext);
        ArgumentNullException.ThrowIfNull(anmerkungen);
        _ = betreff;
        _ = text;
        _ = fortschritt;
        _ = cancellationToken;

        return Task.FromException<string>(
            new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet."));
    }
}

/// <summary>Fragt ein Modell — auf Bitte, einmal, und behält nichts.</summary>
/// <remarks>
/// <strong>Was gespeichert wird, ist das Ergebnis</strong>, weil es das
/// Dokument ist (ADR-0034). Die Eingabe an das Modell wird es nicht: kein
/// Prompt-Archiv, keine Einbettungen, kein Gedaechtnis ueber Bewerbungen
/// hinweg. Ein Modell, das „aus den letzten zwanzig Bewerbungen gelernt" haette,
/// traefe Aussagen ueber die Person, die sie nie getroffen hat.
/// </remarks>
public sealed class HttpAnschreiber(
    IHttpClientFactory fabrik, IOptions<Anschreibeneinstellungen> einstellungen) : IAnschreiber
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "anschreiben";

    /// <summary>
    /// Obergrenze der Antwort. Streaming trägt die Wartezeit; 1200 Tokens
    /// reichen für 250 Wörter. 1500 haben ein lokales 7B früher über die
    /// Minute gedrückt, bevor der Strom Stück für Stück ankam.
    /// </summary>
    public const int HoechsteToken = 1200;

    private readonly Anschreibeneinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public Task<string> SchreibeAsync(
        KiZugang zugang,
        Anschreibenkontext kontext,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zugang);
        ArgumentNullException.ThrowIfNull(kontext);

        return FrageAsync(zugang, Auftrag(kontext), fortschritt, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> UeberarbeiteAsync(
        KiZugang zugang,
        Anschreibenkontext kontext,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zugang);
        ArgumentNullException.ThrowIfNull(kontext);
        ArgumentNullException.ThrowIfNull(anmerkungen);

        var bau = new StringBuilder(Auftrag(kontext));

        bau.AppendLine().AppendLine("## Das bisherige Anschreiben");
        bau.AppendLine($"BETREFF: {betreff}");
        bau.AppendLine("---");
        bau.AppendLine(text);
        bau.AppendLine();
        bau.AppendLine("## Anmerkungen des Bewerbers — ALLE umsetzen");
        bau.AppendLine(
            "Behalte alles bei, was nicht angemerkt wurde. Tonfall und Sprache bleiben.");

        for (var nummer = 0; nummer < anmerkungen.Count; nummer++)
        {
            bau.AppendLine($"{nummer + 1}. {anmerkungen[nummer]}");
        }

        return FrageAsync(zugang, bau.ToString(), fortschritt, cancellationToken);
    }

    /// <summary>Der Auftrag, aus dem Kontext und sonst nichts.</summary>
    private static string Auftrag(Anschreibenkontext kontext)
    {
        var bau = new StringBuilder();

        bau.AppendLine("## Die Stelle (aus der öffentlichen Anzeige)");
        bau.AppendLine($"- Titel: {kontext.StellenTitel}");
        bau.AppendLine($"- Unternehmen: {kontext.Unternehmen}");
        bau.AppendLine($"- Ort: {kontext.StellenOrt}");
        bau.AppendLine($"- Gesucht: {string.Join(", ", kontext.GesuchteFaehigkeiten)}");
        bau.AppendLine($"- Beschreibung: {kontext.StellenBeschreibung}");
        bau.AppendLine();
        bau.AppendLine("## Die bewerbende Person (ihre eigenen Angaben)");
        bau.AppendLine($"- Voller Name (Signatur, GENAU so): {kontext.EigenerName}");
        bau.AppendLine($"- Überschrift: {kontext.EigeneUeberschrift}");
        bau.AppendLine($"- Über sich: {kontext.EigenerText}");
        bau.AppendLine($"- Genannte Fähigkeiten: {string.Join(", ", kontext.EigeneFaehigkeiten)}");
        bau.AppendLine($"- Sprache: {kontext.Sprache} (in dieser Sprache, grammatisch korrekt)");
        bau.AppendLine();
        bau.AppendLine("## Werdegang");

        if (kontext.EigenerWerdegang.Count == 0)
        {
            bau.AppendLine("(kein Werdegang hinterlegt — erfinde keinen)");
        }

        foreach (var station in kontext.EigenerWerdegang)
        {
            bau.AppendLine(
                $"- {station.Zeitraum}: {station.Titel} bei {station.Arbeitgeber}"
                + $" ({string.Join(", ", station.Technologien)}) — {station.Beschreibung}");
        }

        return bau.ToString();
    }

    private async Task<string> FrageAsync(
        KiZugang zugang,
        string auftrag,
        Func<string, Task>? fortschritt,
        CancellationToken cancellationToken)
    {
        if (!zugang.IstEingerichtet)
        {
            throw new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
        }

        using var client = fabrik.CreateClient(Klient);

        // Mit ResponseHeadersRead gilt dieses Limit bis zum ersten Byte, nicht
        // bis zum letzten Token. Sonst stirbt ein lokales Modell nach einer
        // Minute, obwohl es schon geschrieben hat — gemessen an Ollama.
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = BaueAnfrage(zugang, auftrag);

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(
                anfrage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            throw new AnschreibenNichtVerfuegbar(
                $"Der Entwurfsanbieter antwortet nicht ({fehler.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnschreibenNichtVerfuegbar(
                "Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).");
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new AnschreibenNichtVerfuegbar(
                    $"Der Entwurfsanbieter antwortet mit {(int)antwort.StatusCode}.");
            }

            try
            {
                await using var strom = await antwort.Content.ReadAsStreamAsync(cancellationToken);
                var text = await LiesStromAsync(
                    zugang.Anbieter, strom, _einstellungen.Zeitueberschreitung,
                    fortschritt, cancellationToken);

                return text is { Length: > 0 }
                    ? text
                    : throw new AnschreibenNichtVerfuegbar(
                        "Der Entwurfsanbieter sandte einen leeren Text.");
            }
            catch (AnschreibenNichtVerfuegbar)
            {
                throw;
            }
            catch (Exception fehler)
                when (fehler is JsonException or KeyNotFoundException or IndexOutOfRangeException
                      or InvalidOperationException)
            {
                throw new AnschreibenNichtVerfuegbar(
                    "Der Entwurfsanbieter sandte eine unbrauchbare Antwort.");
            }
        }
    }

    private static HttpRequestMessage BaueAnfrage(KiZugang zugang, string auftrag)
    {
        if (zugang.Anbieter == "openai_compatible")
        {
            var anfrage = new HttpRequestMessage(HttpMethod.Post, zugang.Adresse)
            {
                Content = JsonContent.Create(new
                {
                    model = zugang.Modell,
                    max_tokens = HoechsteToken,
                    stream = true,
                    messages = new[]
                    {
                        new { role = "system", content = Anschreibenkontext.Regeln },
                        new { role = "user", content = auftrag }
                    }
                })
            };

            if (zugang.Schluessel.Length > 0)
            {
                anfrage.Headers.TryAddWithoutValidation(
                    "Authorization", $"Bearer {zugang.Schluessel}");
            }

            return anfrage;
        }

        var anthropic = new HttpRequestMessage(HttpMethod.Post, zugang.Adresse)
        {
            Content = JsonContent.Create(new
            {
                model = zugang.Modell,
                max_tokens = HoechsteToken,
                stream = true,
                system = Anschreibenkontext.Regeln,
                messages = new[] { new { role = "user", content = auftrag } }
            })
        };

        anthropic.Headers.Add("x-api-key", zugang.Schluessel);
        anthropic.Headers.Add("anthropic-version", "2023-06-01");
        return anthropic;
    }

    /// <summary>
    /// Liest Server-Sent Events oder ein einziges JSON — OpenAI-kompatibel
    /// und Anthropic. Ein Token nach dem anderen, nicht erst das Ganze.
    /// </summary>
    /// <remarks>
    /// Zwischen zwei Zeilen darf so lange Pause sein wie das Zeitlimit, nicht
    /// für den ganzen Brief. Ein lokales Modell, das langsam schreibt, aber
    /// schreibt, darf fertig werden. Kommt gar nichts mehr, und es liegt schon
    /// ein Anschreiben da, gilt das Stück — sonst wäre eine Minute Schreiben
    /// wieder ein leerer Fehlschlag.
    /// </remarks>
    public static async Task<string> LiesStromAsync(
        string anbieter,
        Stream strom,
        TimeSpan stall,
        Func<string, Task>? fortschritt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anbieter);
        ArgumentNullException.ThrowIfNull(strom);

        using var stallToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Erstes Token darf länger dauern als die Pause zwischen zwei. Leere
        // SSE-Zeilen dürfen die Uhr NICHT zurücksetzen — sonst läuft ein
        // stummer Strom minutenlang, ohne dass je ein Buchstabe ankommt.
        stallToken.CancelAfter(stall);

        using var leser = new StreamReader(strom);
        var text = new StringBuilder();
        var roh = new StringBuilder();

        try
        {
            while (await leser.ReadLineAsync(stallToken.Token) is { } zeile)
            {

                if (zeile.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = zeile["data:".Length..].Trim();

                    if (payload.Length == 0)
                    {
                        continue;
                    }

                    if (payload == "[DONE]")
                    {
                        break;
                    }

                    await NimmDeltaAsync(
                        anbieter, payload, text, stallToken, stall, fortschritt);
                    continue;
                }

                if (zeile.StartsWith("event:", StringComparison.OrdinalIgnoreCase)
                    || zeile.StartsWith(':')
                    || zeile.Length == 0)
                {
                    continue;
                }

                // Ollama nativ und andere NDJSON-Anbieter: eine JSON-Zeile
                // je Token, ohne `data:`-Präfix. Ohne diesen Zweig landet
                // alles in `roh` und der Brief erscheint erst am Ende.
                if (zeile[0] == '{')
                {
                    await NimmDeltaAsync(
                        anbieter, zeile, text, stallToken, stall, fortschritt);
                    continue;
                }

                roh.Append(zeile);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (text.Length > 0)
            {
                return text.ToString();
            }

            throw new AnschreibenNichtVerfuegbar(
                "Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).");
        }

        if (text.Length > 0)
        {
            return text.ToString();
        }

        if (roh.Length == 0)
        {
            return string.Empty;
        }

        using var gelesen = JsonDocument.Parse(roh.ToString());

        return LiesText(anbieter, gelesen.RootElement) ?? string.Empty;
    }

    private static async Task NimmDeltaAsync(
        string anbieter,
        string json,
        StringBuilder text,
        CancellationTokenSource stallToken,
        TimeSpan stall,
        Func<string, Task>? fortschritt)
    {
        string? stueck;

        try
        {
            stueck = LiesDelta(anbieter, json);
        }
        catch (JsonException)
        {
            return;
        }

        if (stueck is not { Length: > 0 })
        {
            return;
        }

        text.Append(stueck);
        stallToken.CancelAfter(stall);

        if (fortschritt is not null)
        {
            await fortschritt(text.ToString());
        }
    }

    /// <summary>Ein Token aus einem SSE-Stück — oder null, wenn keins drin ist.</summary>
    public static string? LiesDelta(string anbieter, string json)
    {
        ArgumentNullException.ThrowIfNull(anbieter);
        ArgumentException.ThrowIfNullOrEmpty(json);

        using var gelesen = JsonDocument.Parse(json);

        return LiesDelta(anbieter, gelesen.RootElement);
    }

    private static string? LiesDelta(string anbieter, JsonElement wurzel)
    {
        if (anbieter == "openai_compatible")
        {
            if (wurzel.TryGetProperty("choices", out var auswahl) && auswahl.GetArrayLength() > 0)
            {
                var erstes = auswahl[0];

                if (erstes.TryGetProperty("delta", out var delta)
                    && delta.TryGetProperty("content", out var inhalt)
                    && inhalt.ValueKind == JsonValueKind.String)
                {
                    return inhalt.GetString();
                }

                if (erstes.TryGetProperty("message", out var nachricht)
                    && nachricht.TryGetProperty("content", out var ganzes)
                    && ganzes.ValueKind == JsonValueKind.String)
                {
                    return ganzes.GetString();
                }
            }

            // Ollama `/api/chat`: { "message": { "content": "…" }, "done": false }
            if (wurzel.TryGetProperty("message", out var ollama)
                && ollama.TryGetProperty("content", out var ollamaText)
                && ollamaText.ValueKind == JsonValueKind.String)
            {
                return ollamaText.GetString();
            }

            // Ollama `/api/generate`: { "response": "…", "done": false }
            if (wurzel.TryGetProperty("response", out var generiert)
                && generiert.ValueKind == JsonValueKind.String)
            {
                return generiert.GetString();
            }

            return null;
        }

        if (wurzel.TryGetProperty("type", out var typ)
            && typ.GetString() == "content_block_delta"
            && wurzel.TryGetProperty("delta", out var anthropic)
            && anthropic.TryGetProperty("text", out var stueck)
            && stueck.ValueKind == JsonValueKind.String)
        {
            return stueck.GetString();
        }

        return null;
    }

    private static string? LiesText(string anbieter, JsonElement wurzel)
    {
        if (anbieter == "openai_compatible")
        {
            return wurzel
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        return wurzel.GetProperty("content")[0].GetProperty("text").GetString();
    }
}
