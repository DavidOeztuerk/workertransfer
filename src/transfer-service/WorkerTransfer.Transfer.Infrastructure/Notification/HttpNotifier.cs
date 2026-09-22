using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Transfer.Infrastructure.Notification;

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
    /// einem Ende irgendwann — und er haengt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The shared secret. Expressly not the erasure's.</summary>
    /// <remarks>
    /// Empty means: nothing is delivered — and as a <em>failure</em>, so the row
    /// stays due instead of counting as sent.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Hands an intent on to notification-service.</summary>
/// <remarks>
/// Throws on a transport error <em>and</em> on a bad status — that is what
/// gives <c>delivered_at</c> a meaning at all. The failure is the outbox's
/// business: it counts the attempt, keeps the row and tries again.
/// </remarks>
public sealed class HttpNotifier(
    IHttpClientFactory factory,
    IOptions<NotificationSettings> settings) : IDelivery
{
    private readonly NotificationSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task DeliverAsync(
        SubjectId recipient,
        string kind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new InvalidOperationException("Notifications:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_settings.Geheimnis))
        {
            throw new InvalidOperationException("Notifications:Geheimnis ist nicht gesetzt.");
        }

        using var client = factory.CreateClient(nameof(HttpNotifier));

        client.Timeout = _settings.Zeitueberschreitung;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.Adresse), "/internal/notifications"))
        {
            // The whole content: about whom and of which kind. No text, no
            // reason, no company name — the recipient reads the current state
            // itself, which is why a second delivery is harmless too.
            Content = JsonContent.Create(
                // `user_id`, not `userId`. The recipient declares
                // [JsonPropertyName("user_id")]; camelCase bound silently to
                // Guid.Empty, and the service answered 500. Measured: the whole
                // notification path was dead — 18 outbox rows, all given up
                // after ten attempts.
                new { user_id = recipient.Value, kind = kind })
        };

        request.Headers.Add("X-Notify-Secret", _settings.Geheimnis);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
