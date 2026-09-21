using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WorkerTransfer.Jobs.Application.Ports;

namespace WorkerTransfer.Jobs.Infrastructure.Entwurf;

/// <summary>Ob und wohin überhaupt ein Modell gefragt wird.</summary>
public sealed class DraftSettings
{
    /// <summary>Der Section in der Konfiguration.</summary>
    public const string Section = "Draft";

    /// <summary>
    /// Leer heißt: es wird kein Fremddienst gerufen, und die Oberfläche sagt das.
    /// </summary>
    public string Schluessel { get; set; } = string.Empty;

    /// <summary>Die Adresse des Anbieters.</summary>
    public string Adresse { get; set; } = "https://api.anthropic.com/v1/messages";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Dreissig und nicht fuenf Sekunden: am anderen Ende steht ein
    /// Sprachmodell, und ein Entwurf braucht laenger als eine Abfrage. Aber
    /// eben nicht unbegrenzt — ohne diese Zeile gilt die Vorgabe von
    /// <c>HttpClient</c> mit HUNDERT Sekunden, und so lange soll niemand
    /// vor einem Formular sitzen.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Welches Modell.</summary>
    public string Modell { get; set; } = "claude-haiku-4-5-20251001";
}

/// <summary>Der Entwerfer, wenn keiner eingerichtet ist.</summary>
/// <remarks>
/// Er liefert keine ResumeTemplate, sondern sagt, dass es ihn nicht gibt. Ein Entwurf,
/// der nicht vom Modell kommt, aber so aussieht, wäre die schlechtere Antwort.
/// </remarks>
public sealed class NoDrafter : IDrafter
{
    /// <inheritdoc />
    public Task<string> DraftAsync(
        AdvertDraft draft, CancellationToken cancellationToken = default) =>
        throw new DraftUnavailable("Es ist kein Entwurfsanbieter eingerichtet.");
}

/// <summary>Fragt ein Modell, einmal, und behält nichts.</summary>
public sealed class HttpDrafter(
    IHttpClientFactory fabrik, IOptions<DraftSettings> einstellungen) : IDrafter
{
    private readonly DraftSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        AdvertDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (string.IsNullOrEmpty(_einstellungen.Schluessel))
        {
            throw new DraftUnavailable("Es ist kein Entwurfsanbieter eingerichtet.");
        }

        using var client = fabrik.CreateClient(nameof(HttpDrafter));

        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var request = new HttpRequestMessage(HttpMethod.Post, _einstellungen.Adresse)
        {
            // Was hinausgeht, steht in AdvertDraft und nirgends sonst:
            // keine tenant_id, kein Firmenname, nichts über einen Menschen.
            Content = JsonContent.Create(new
            {
                model = _einstellungen.Modell,
                max_tokens = 800,
                system = AdvertDraft.Regeln,
                messages = new[] { new { role = "user", content = draft.Prompt } }
            })
        };

        request.Headers.Add("x-api-key", _einstellungen.Schluessel);
        request.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // Die Art, nie der Inhalt.
            throw new DraftUnavailable(
                $"Der Entwurfsanbieter antwortet nicht ({error.GetType().Name}).");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Ein abgelaufenes `client.Timeout` kommt als TaskCanceledException
            // und NICHT als HttpRequestException — ohne diesen Zweig verliesse
            // es den Dienst als 500, obwohl es dieselbe Aussage ist wie ein
            // Verbindungsfehler: der Anbieter antwortet nicht.
            //
            // Die Bedingung trennt die beiden Faelle, die sich denselben
            // Ausnahmetyp teilen. Ist das Token des Aufrufers gezogen, hat der
            // Browser aufgelegt — dann ist gar nichts kaputt, und die Ausnahme
            // gehoert weitergereicht statt in eine 503 verwandelt.
            throw new DraftUnavailable(
                "Der Entwurfsanbieter antwortet nicht (Zeitueberschreitung).");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DraftUnavailable(
                    $"Der Entwurfsanbieter antwortet mit {(int)response.StatusCode}.");
            }

            using var gelesen = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return gelesen.RootElement.TryGetProperty("content", out var content)
                   && content.GetArrayLength() > 0
                   && content[0].TryGetProperty("text", out var text)
                ? text.GetString() ?? string.Empty
                : throw new DraftUnavailable(
                    "Der Entwurfsanbieter antwortet in unerwarteter Form.");
        }
    }
}
