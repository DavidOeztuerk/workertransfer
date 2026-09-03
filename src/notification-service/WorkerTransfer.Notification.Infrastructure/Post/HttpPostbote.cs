using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Ports;

namespace WorkerTransfer.Notification.Infrastructure.Post;

/// <summary>Wo identity-service antwortet, und was uns als uns ausweist.</summary>
public sealed class Posteinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Identity";

    /// <summary>Die Basisadresse von identity-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Das gemeinsame Geheimnis. Ausdrücklich nicht das der Löschung.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Bittet identity-service, den einen Satz hinauszuschicken.</summary>
/// <remarks>
/// <strong>Feuern und vergessen.</strong> Ein Fehlschlag hier darf niemals den
/// Vorgang kippen, der ihn ausgelöst hat — die genaue Umkehrung der
/// Einwilligungsregel, und aus demselben Grund richtig: beim Ledger geht es um
/// Erlaubnis (im Zweifel nein), hier um Höflichkeit. Einen Widerruf
/// zurückzurollen, weil eine Mail nicht rausging, wäre grotesk.
/// <para>
/// Der Rumpf trägt <strong>nur die Kennung</strong>. Weder die Art noch ein
/// Text: identity-service formuliert den einen festen Satz, und ein Aufrufer,
/// der etwas beisteuern könnte, wäre der Anfang von „nur diese eine Zeile
/// noch".
/// </para>
/// </remarks>
public sealed class HttpPostbote(
    IHttpClientFactory fabrik,
    IOptions<Posteinstellungen> einstellungen,
    ILogger<HttpPostbote> protokoll) : IPostbote
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "identity";

    private readonly Posteinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task SchickeAsync(SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse)
            || string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            // Kein Geheimnis, kein Versuch: der Endpunkt wäre ohnehin zu, und
            // ein Aufruf ins Leere kostet bei jedem Vorgang eine
            // Zeitüberschreitung.
            return;
        }

        try
        {
            using var client = fabrik.CreateClient(Klient);
            client.Timeout = _einstellungen.Zeitueberschreitung;

            using var anfrage = new HttpRequestMessage(
                HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/internal/notify"))
            {
                // `user_id`, nicht `userId`. identity-service deklariert
                // [JsonPropertyName("user_id")]; camelCase band still auf
                // Guid.Empty. Dieser Fehlschlag ist der stillste im System —
                // er wirft nicht, er protokolliert eine Warnung, und der
                // Ausgangskorb sieht ihn nie. Gemessen: im Postfach standen
                // ausschliesslich Bestaetigungsmails, die identity selbst
                // verschickt.
                Content = JsonContent.Create(new { user_id = wer.Value })
            };

            anfrage.Headers.Add("X-Notify-Secret", _einstellungen.Geheimnis);

            using var antwort = await client.SendAsync(anfrage, cancellationToken);

            if (!antwort.IsSuccessStatusCode)
            {
                // Protokolliert wird die Art des Fehlschlags, nie der Empfänger:
                // ein Protokoll ist auch ein Ort, an dem etwas landet.
                protokoll.LogWarning(
                    "identity-service antwortete mit {Code}", (int)antwort.StatusCode);
            }
        }
        catch (Exception fehler) when (fehler is not OperationCanceledException)
        {
            // Schluckt jeden Fehler. Das ist der Zweck, nicht eine
            // Nachlässigkeit — die Drossel ist bereits gesetzt, diese Nachricht
            // ist verloren, und das ist die richtige Richtung.
            protokoll.LogWarning(
                "Benachrichtigung konnte nicht angestoßen werden ({Art})", fehler.GetType().Name);
        }
    }
}
