using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Infrastructure.Benachrichtigung;

/// <summary>Wohin Benachrichtigungen gehen, und was sie als unsere ausweist.</summary>
public sealed class Benachrichtigungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Notifications";

    /// <summary>Die Basisadresse des notification-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fuenf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier.
    /// OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT
    /// Sekunden. Das ist kein Zeitlimit, das ist ein haengender Aufruf mit
    /// einem Ende irgendwann — und er haengt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Das gemeinsame Geheimnis. Ausdrücklich nicht das der Löschung.</summary>
    /// <remarks>
    /// Leer heißt: es wird nichts zugestellt — und zwar als <em>Fehlschlag</em>,
    /// damit die Zeile fällig bleibt, statt als gesendet zu gelten.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Reicht eine Absicht an den notification-service.</summary>
/// <remarks>
/// Wirft bei Transportfehler <em>und</em> bei schlechtem Status — das ist, was
/// <c>delivered_at</c> überhaupt eine Bedeutung gibt. Der Fehlschlag ist Sache
/// der Outbox: sie zählt den Versuch, behält die Zeile und versucht es wieder.
/// </remarks>
public sealed class HttpBenachrichtigung(
    IHttpClientFactory fabrik,
    IOptions<Benachrichtigungseinstellungen> einstellungen) : IZustellung
{
    private readonly Benachrichtigungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task ZustelleAsync(
        SubjectId empfaenger,
        string art,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new InvalidOperationException("Notifications:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new InvalidOperationException("Notifications:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(nameof(HttpBenachrichtigung));

        client.Timeout = _einstellungen.Zeitueberschreitung;
        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/internal/notifications"))
        {
            // Der ganze Inhalt: über wen und welcher Art. Kein Text, kein
            // Grund, kein Firmenname — der Empfänger liest den aktuellen Stand
            // selbst, weshalb eine zweite Zustellung auch harmlos ist.
            Content = JsonContent.Create(new { userId = empfaenger.Value, kind = art })
        };

        anfrage.Headers.Add("X-Notify-Secret", _einstellungen.Geheimnis);

        using var antwort = await client.SendAsync(anfrage, cancellationToken);

        antwort.EnsureSuccessStatusCode();
    }
}
