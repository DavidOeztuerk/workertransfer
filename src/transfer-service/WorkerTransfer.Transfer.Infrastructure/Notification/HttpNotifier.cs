using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Transfer.Infrastructure.Notification;

/// <summary>Where notifications go, and what identifies them as ours.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Notifications__Adresse</c>,
/// <c>…__Zeitueberschreitung</c>, <c>…__Geheimnis</c>), so they stay German
/// until the keys are renamed everywhere at once — in compose, in the chart and
/// in <c>.env.example</c>.
/// </remarks>
public sealed class NotificationSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Notifications";

    /// <summary>The base address of notification-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, as with every other service-to-service call here. WITHOUT
    /// this line <c>HttpClient</c>'s default applies: a HUNDRED seconds. That is
    /// no time limit but a hanging call with an end some time — and it hangs the
    /// caller with it.
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
            throw new InvalidOperationException("Notifications:Adresse is not configured.");
        }

        if (string.IsNullOrEmpty(_settings.Geheimnis))
        {
            throw new InvalidOperationException("Notifications:Geheimnis is not configured.");
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
