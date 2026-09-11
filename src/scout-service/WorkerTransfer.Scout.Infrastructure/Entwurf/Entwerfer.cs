using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Entwurf;

/// <summary>Ob und wohin überhaupt ein Modell gefragt wird.</summary>
public sealed class Entwurfseinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Draft";

    /// <summary>
    /// Leer heisst: es wird kein Fremddienst gerufen, und die Oberfläche sagt das.
    /// </summary>
    /// <remarks>
    /// Die Voreinstellung ist ausdrücklich <em>kein</em> Anbieter. Digitale
    /// Souveränität heisst hier ganz praktisch: ohne dass jemand einen Schlüssel
    /// hinterlegt, verlässt kein Wort dieses Systems das System.
    /// </remarks>
    public string Schluessel { get; set; } = string.Empty;

    /// <summary>Die Adresse des Anbieters.</summary>
    /// <remarks>
    /// Sie gehört in die Umgebung und nicht nur hierher: eine eingebaute
    /// Zieladresse nach draussen ist genau das, was ein Souveränitätsbericht
    /// sichtbar machen soll — und die Egress-Grenze liest ihre erlaubten Hosts
    /// aus der Konfiguration.
    /// </remarks>
    public string Adresse { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Dreissig und nicht fünf Sekunden: am anderen Ende steht ein Sprachmodell,
    /// und ein Entwurf braucht länger als eine Abfrage. Aber eben nicht
    /// unbegrenzt — ohne diese Zeile gilt die Vorgabe von <c>HttpClient</c> mit
    /// HUNDERT Sekunden, und so lange soll niemand vor einem Formular sitzen.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Welches Modell.</summary>
    public string Modell { get; set; } = "claude-haiku-4-5-20251001";
}

/// <summary>Der Entwerfer, wenn keiner eingerichtet ist.</summary>
/// <remarks>
/// Er schweigt nicht und liefert auch keine Vorlage — er sagt, dass es ihn
/// nicht gibt. Ein Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre
/// die schlechtere Antwort: jemand hielte ihn für einen Vorschlag und schickte
/// ihn an einen Menschen.
/// </remarks>
public sealed class KeinEntwerfer : IEntwerfer
{
    /// <inheritdoc />
    public Task<string> EntwirfAsync(
        Ansprachelage lage, CancellationToken cancellationToken = default) =>
        throw new AnspracheNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
}

/// <summary>Fragt ein Modell, einmal, und behält nichts.</summary>
/// <remarks>
/// Ein Aufruf, ein Text. Kein zweiter Durchgang, kein Gedächtnis, kein
/// Vektorspeicher — und keine Zeile irgendwo darüber, dass gefragt wurde
/// (ADR-0024). <strong>Und kein Versand:</strong> was hier zurückkommt, geht an
/// den Browser dessen, der gefragt hat. Dieser Dienst schreibt niemandem
/// (ADR-0036 Auflage 4).
/// </remarks>
public sealed class HttpEntwerfer(
    IHttpClientFactory fabrik,
    IOptions<Entwurfseinstellungen> einstellungen) : IEntwerfer
{
    private readonly Entwurfseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<string> EntwirfAsync(
        Ansprachelage lage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lage);

        if (string.IsNullOrEmpty(_einstellungen.Schluessel))
        {
            throw new AnspracheNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
        }

        using var client = fabrik.CreateClient(nameof(HttpEntwerfer));

        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(HttpMethod.Post, _einstellungen.Adresse)
        {
            // Was hinausgeht, steht in Ansprachelage und nirgends sonst: kein
            // Name, keine Adresse, keine SubjectId, kein Arbeitgeber, kein
            // Firmenname — und keine Belege.
            Content = JsonContent.Create(new
            {
                model = _einstellungen.Modell,
                max_tokens = 400,
                system = Ansprachelage.Regeln,
                messages = new[] { new { role = "user", content = lage.Prompt } }
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
            // Die Art, nie der Inhalt. Ein Fehlschlag ist genau der Moment, in
            // dem jemand den Prompt sehen will, und genau der, in dem ihn
            // aufzuschreiben am schlechtesten ist.
            throw new AnspracheNichtVerfuegbar(
                $"Der Entwurfsanbieter antwortet nicht ({fehler.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Ein abgelaufenes `client.Timeout` kommt als TaskCanceledException
            // und NICHT als HttpRequestException — ohne diesen Zweig verliesse
            // es den Dienst als 500, obwohl es dieselbe Aussage ist.
            throw new AnspracheNichtVerfuegbar(
                "Der Entwurfsanbieter antwortet nicht (Zeitueberschreitung).");
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new AnspracheNichtVerfuegbar(
                    $"Der Entwurfsanbieter antwortet mit {(int)antwort.StatusCode}.");
            }

            using var gelesen = JsonDocument.Parse(
                await antwort.Content.ReadAsStringAsync(cancellationToken));

            return gelesen.RootElement.TryGetProperty("content", out var inhalt)
                   && inhalt.GetArrayLength() > 0
                   && inhalt[0].TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : throw new AnspracheNichtVerfuegbar(
                    "Der Entwurfsanbieter antwortet in unerwarteter Form.");
        }
    }
}
