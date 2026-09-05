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
    public bool Eingerichtet => false;

    /// <inheritdoc />
    public Task<string> SchreibeAsync(
        Anschreibenkontext kontext, CancellationToken cancellationToken = default) =>
        throw new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");

    /// <inheritdoc />
    public Task<string> UeberarbeiteAsync(
        Anschreibenkontext kontext,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
        CancellationToken cancellationToken = default) =>
        throw new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
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

    private readonly Anschreibeneinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public bool Eingerichtet => _einstellungen.Schluessel.Length > 0;

    /// <inheritdoc />
    public Task<string> SchreibeAsync(
        Anschreibenkontext kontext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kontext);

        return FrageAsync(Auftrag(kontext), cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> UeberarbeiteAsync(
        Anschreibenkontext kontext,
        string betreff,
        string text,
        IReadOnlyList<string> anmerkungen,
        CancellationToken cancellationToken = default)
    {
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

        return FrageAsync(bau.ToString(), cancellationToken);
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
        bau.AppendLine($"- Name: {kontext.EigenerName}");
        bau.AppendLine($"- Überschrift: {kontext.EigeneUeberschrift}");
        bau.AppendLine($"- Über sich: {kontext.EigenerText}");
        bau.AppendLine($"- Genannte Fähigkeiten: {string.Join(", ", kontext.EigeneFaehigkeiten)}");
        bau.AppendLine($"- Sprache: {kontext.Sprache}");
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

    private async Task<string> FrageAsync(string auftrag, CancellationToken cancellationToken)
    {
        if (!Eingerichtet)
        {
            throw new AnschreibenNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
        }

        using var client = fabrik.CreateClient(Klient);

        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(HttpMethod.Post, _einstellungen.Adresse)
        {
            Content = JsonContent.Create(new
            {
                model = _einstellungen.Modell,
                max_tokens = 1500,
                system = Anschreibenkontext.Regeln,
                messages = new[] { new { role = "user", content = auftrag } }
            })
        };

        anfrage.Headers.Add("x-api-key", _einstellungen.Schluessel);
        anfrage.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            // Die Art, nie der Inhalt.
            throw new AnschreibenNichtVerfuegbar(
                $"Der Entwurfsanbieter antwortet nicht ({fehler.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Ein abgelaufenes `client.Timeout` kommt als TaskCanceledException
            // und nicht als HttpRequestException. Die Bedingung trennt es vom
            // Fall „der Browser hat aufgelegt" — dann ist nichts kaputt.
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
                using var gelesen = await JsonDocument.ParseAsync(
                    await antwort.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);

                var text = gelesen.RootElement
                    .GetProperty("content")[0]
                    .GetProperty("text")
                    .GetString();

                return text is { Length: > 0 }
                    ? text
                    : throw new AnschreibenNichtVerfuegbar(
                        "Der Entwurfsanbieter sandte einen leeren Text.");
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
}
