using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Resume.Infrastructure.Notification;

/// <summary>Where notifications go, and what proves they are ours.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Notifications__BaseUrl</c>,
/// <c>…__Timeout</c>, <c>…__Secret</c>); renaming one alone leaves its value on the
/// default, silently.
/// </remarks>
public sealed class NotificationSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Notifications";

    /// <summary>The base address of notification-service.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, like every other service-to-service call here. WITHOUT
    /// this line <c>HttpClient</c>'s default applies: a HUNDRED seconds. That is
    /// not a time limit, that is a hanging call with an end some time — and it
    /// hangs the caller along with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The shared secret. Expressly a different one from the erasure secret.
    /// </summary>
    /// <remarks>
    /// "May trigger a mail" and "may delete everything about a person" must not
    /// be the same piece of paper. Empty means nothing is delivered — and as a
    /// <em>failure</em>, so the row stays due instead of counting as sent.
    /// </remarks>
    public string Secret { get; set; } = string.Empty;
}

/// <summary>Hands one intent to notification-service.</summary>
/// <remarks>
/// Throws on a transport error <em>and</em> on a bad status, which is what
/// makes <c>delivered_at</c> mean something. The failure is the outbox's
/// business: it counts the attempt, keeps the row, and tries again.
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
            // The whole payload: whom it is about and what kind. No text, no
            // reason, no company name — the recipient reads the current state
            // itself, which is also why a redelivery is harmless.
            Content = JsonContent.Create(
                // `user_id`, not `userId`. The receiver declares
                // [JsonPropertyName("user_id")]; camelCase bound silently to
                // Guid.Empty, and the service answered 500. Measured: the whole
                // notification path was dead — 18 outbox rows, all given up
                // after ten attempts, not a single mail.
                new { user_id = recipient.Value, kind = kind })
        };

        request.Headers.Add("X-Notify-Secret", _settings.Secret);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
