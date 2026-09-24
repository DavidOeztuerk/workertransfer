using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Contracts;

namespace WorkerTransfer.Scout.Infrastructure.Notification;

/// <summary>Where notifications go, and what identifies them as ours.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Notifications__Adresse</c>, <c>Notifications__Zeitueberschreitung</c>,
/// <c>Notifications__Geheimnis</c>), so they stay German until the keys are
/// renamed everywhere at once — in compose, in the chart and in
/// <c>.env.example</c>.
/// </remarks>
public sealed class NotificationSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Notifications";

    /// <summary>The base address of notification-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, like every other service-to-service call here. WITHOUT this
    /// line <c>HttpClient</c>'s default applies: a HUNDRED seconds. That is no
    /// time limit, that is a hanging call with an end at some point — and here it
    /// hangs the outbox loop with it.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The shared secret. Expressly not the erasure's.</summary>
    /// <remarks>
    /// Empty means: nothing is delivered — as a <em>failure</em>, so the row
    /// stays due instead of counting as sent.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Hands "your profile was discovered" to notification-service.</summary>
/// <remarks>
/// <para>Throws on a transport error <em>and</em> on a bad status — that is what
/// gives <c>delivered_at</c> a meaning at all. The failure is the outbox's
/// business: it counts the attempt, keeps the row and tries again.</para>
///
/// <para><strong>A second delivery is harmless</strong>, and the cap is built on
/// that: at most one message per person and day is decided by the recipient from
/// its own inbox. A cap here would be a table "this person was reported on …" —
/// again a register of a human being's visibility (ADR-0033).</para>
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
            throw new InvalidOperationException("Notifications:Adresse is not set.");
        }

        if (string.IsNullOrEmpty(_settings.Geheimnis))
        {
            throw new InvalidOperationException("Notifications:Geheimnis is not set.");
        }

        using var client = factory.CreateClient(nameof(HttpNotifier));

        client.Timeout = _settings.Zeitueberschreitung;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.Adresse), "/internal/notifications"))
        {
            // A TYPED CONTRACT, no anonymous object. `user_id` and not `userId`:
            // the receiver declares [JsonPropertyName("user_id")], camelCase
            // silently bound to Guid.Empty there — measured, and the whole
            // notification path was dead as a result.
            //
            // The whole content: about whom and of which kind. NO company name
            // (ADR-0033), no text, no reason.
            Content = JsonContent.Create(new DiscoveryNotificationV1(recipient.Value, kind))
        };

        request.Headers.Add("X-Notify-Secret", _settings.Geheimnis);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
