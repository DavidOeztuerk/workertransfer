using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Transfer.Infrastructure.Notification;

/// <summary>Where notifications go, and what identifies them as ours.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Notifications__BaseUrl</c>,
/// <c>…__Timeout</c>, <c>…__Secret</c>); renaming one alone leaves its value on the
/// default, silently.
/// </remarks>
public sealed class NotificationSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Notifications";

    /// <summary>The base address of notification-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, as with every other service-to-service call here. WITHOUT
    /// this line <c>HttpClient</c>'s default applies: a HUNDRED seconds. That is
    /// no time limit but a hanging call with an end some time — and it hangs the
    /// caller with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The shared secret. Expressly not the erasure's.</summary>
    /// <remarks>
    /// Empty means: nothing is delivered — and as a <em>failure</em>, so the row
    /// stays due instead of counting as sent.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
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
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new InvalidOperationException("Notifications:BaseUrl is not configured.");
        }

        if (string.IsNullOrEmpty(_settings.Secret))
        {
            throw new InvalidOperationException("Notifications:Secret is not configured.");
        }

        using var client = factory.CreateClient(nameof(HttpNotifier));

        client.Timeout = _settings.Timeout;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), "/internal/notifications"))
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

        request.Headers.Add("X-Notify-Secret", _settings.Secret);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
