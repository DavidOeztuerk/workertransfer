using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Entwurf;

/// <summary>Ob und wohin überhaupt ein Modell gefragt wird.</summary>
public sealed class Entwurfseinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Draft";

    /// <summary>
    /// Leer heißt: es wird kein Fremddienst gerufen, und die Oberfläche sagt das.
    /// </summary>
    /// <remarks>
    /// Die Voreinstellung ist ausdrücklich <em>kein</em> Anbieter. Digitale
    /// Souveränität heißt hier ganz praktisch: ohne dass jemand einen Schlüssel
    /// hinterlegt, verlässt kein Wort dieses Systems das System.
    /// </remarks>
    public string Schluessel { get; set; } = string.Empty;

    /// <summary>Die Adresse des Anbieters.</summary>
    public string Adresse { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>Welches Modell.</summary>
    public string Modell { get; set; } = "claude-haiku-4-5-20251001";
}

/// <summary>Der Entwerfer, wenn keiner eingerichtet ist.</summary>
/// <remarks>
/// Er schweigt nicht und liefert auch keine Vorlage — er sagt, dass es ihn
/// nicht gibt. Ein Entwurf, der nicht vom Modell kommt, aber so aussieht, wäre
/// die schlechtere Antwort: die Person hielte ihn für einen Vorschlag und
/// könnte nicht wissen, dass niemand gefragt wurde.
/// </remarks>
public sealed class KeinEntwerfer : IEntwerfer
{
    /// <inheritdoc />
    public Task<string> EntwirfAsync(
        Entwurfslage lage, CancellationToken cancellationToken = default) =>
        throw new EntwurfNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
}

/// <summary>Fragt ein Modell, einmal, und behält nichts.</summary>
/// <remarks>
/// Ein Aufruf, ein Text. Kein zweiter Durchgang, der den Text der Person
/// ungefragt ein zweites Mal umschreibt, und keine Zeile irgendwo darüber, dass
/// gefragt wurde (ADR-0024).
/// </remarks>
public sealed class HttpEntwerfer(
    IHttpClientFactory fabrik,
    IOptions<Entwurfseinstellungen> einstellungen) : IEntwerfer
{
    private readonly Entwurfseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<string> EntwirfAsync(
        Entwurfslage lage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lage);

        if (string.IsNullOrEmpty(_einstellungen.Schluessel))
        {
            throw new EntwurfNichtVerfuegbar("Es ist kein Entwurfsanbieter eingerichtet.");
        }

        using var client = fabrik.CreateClient(nameof(HttpEntwerfer));

        using var anfrage = new HttpRequestMessage(HttpMethod.Post, _einstellungen.Adresse)
        {
            // Was hinausgeht, steht in Entwurfslage und nirgends sonst: kein
            // Name, keine Adresse, keine SubjectId, kein Arbeitgeber.
            Content = JsonContent.Create(new
            {
                model = _einstellungen.Modell,
                max_tokens = 400,
                system = Entwurfslage.Regeln,
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
            throw new EntwurfNichtVerfuegbar(
                $"Der Entwurfsanbieter antwortet nicht ({fehler.GetType().Name}).");
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new EntwurfNichtVerfuegbar(
                    $"Der Entwurfsanbieter antwortet mit {(int)antwort.StatusCode}.");
            }

            using var gelesen = JsonDocument.Parse(
                await antwort.Content.ReadAsStringAsync(cancellationToken));

            return gelesen.RootElement.TryGetProperty("content", out var inhalt)
                   && inhalt.GetArrayLength() > 0
                   && inhalt[0].TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : throw new EntwurfNichtVerfuegbar(
                    "Der Entwurfsanbieter antwortet in unerwarteter Form.");
        }
    }
}
