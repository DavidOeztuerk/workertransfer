using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Contracts;

namespace WorkerTransfer.Scout.Infrastructure.Benachrichtigung;

/// <summary>Wohin Benachrichtigungen gehen, und was sie als unsere ausweist.</summary>
public sealed class NotificationSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Notifications";

    /// <summary>Die Basisadresse des notification-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fuenf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier.
    /// OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT
    /// Sekunden. Das ist kein Zeitlimit, das ist ein haengender Aufruf mit
    /// einem Ende irgendwann — und hier haengt er die Ausgangsschleife mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Das gemeinsame Geheimnis. Ausdrücklich nicht das der Löschung.</summary>
    /// <remarks>
    /// Leer heisst: es wird nichts zugestellt — und zwar als <em>Fehlschlag</em>,
    /// damit die Zeile fällig bleibt, statt als gesendet zu gelten.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Reicht „dein Profile wurde entdeckt" an notification-service.</summary>
/// <remarks>
/// <para>Wirft bei Transportfehler <em>und</em> bei schlechtem Status — das ist,
/// was <c>delivered_at</c> überhaupt eine Bedeutung gibt. Der Fehlschlag ist
/// Sache der Outbox: sie zählt den Versuch, behält die Zeile und versucht es
/// wieder.</para>
///
/// <para><strong>Eine zweite Zustellung ist harmlos</strong>, und darauf ist die
/// Kappe gebaut: höchstens eine Nachricht je Person und Tag entscheidet der
/// Empfänger aus seinem eigenen Inbox. Eine Kappe hier wäre eine Tabelle
/// „diese Person wurde am … gemeldet" — wieder ein Verzeichnis über die
/// Sichtbarkeit eines Menschen (ADR-0033).</para>
/// </remarks>
public sealed class HttpBenachrichtigung(
    IHttpClientFactory fabrik,
    IOptions<NotificationSettings> einstellungen) : IDelivery
{
    private readonly NotificationSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task DeliverAsync(
        SubjectId recipient,
        string kind,
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
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/internal/notifications"))
        {
            // EIN TYPISIERTER VERTRAG, kein anonymes Objekt. `user_id` und nicht
            // `userId`: der Empfaenger deklariert [JsonPropertyName("user_id")],
            // camelCase band dort still auf Guid.Empty — gemessen, und der ganze
            // Benachrichtigungsweg war daraufhin tot.
            //
            // Der ganze Inhalt: ueber wen und welcher Art. KEIN Firmenname
            // (ADR-0033), kein Text, kein Grund.
            Content = JsonContent.Create(new DiscoveryNotificationV1(recipient.Value, kind))
        };

        request.Headers.Add("X-Notify-Secret", _einstellungen.Geheimnis);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
