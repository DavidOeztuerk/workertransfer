using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Assessment.Contracts;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Infrastructure.Notification;

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
    /// Without this line <c>HttpClient</c>'s default applies: a hundred seconds
    /// — and here the outbox loop would hang on it too.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The shared secret. Expressly not the erasure one.</summary>
    /// <remarks>
    /// Empty means nothing is delivered — and as a <em>failure</em>, so the row
    /// stays due instead of counting as sent.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Passes on "something moved on your work sample".</summary>
/// <remarks>
/// <para>Throws on a transport error <em>and</em> on a bad status — that is what
/// gives <c>delivered_at</c> any meaning at all. The failure is the outbox's
/// business: it counts the attempt, keeps the row and tries again. Delivery is
/// at-least-once, and a second one is harmless.</para>
///
/// <para><strong>No task text and no evaluation travel along</strong>, only an
/// id and a kind. A mail lands in an inbox, and that inbox may be the current
/// employer's — a line "your work sample was rejected" there would be exactly
/// the disclosure this platform is built against.</para>
/// </remarks>
public sealed class HttpNotifier(
    IHttpClientFactory factory,
    IOptions<NotificationSettings> settings) : IDelivery
{
    private readonly NotificationSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
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
            Content = JsonContent.Create(new WorkSampleNotificationV1(recipient.Value, kind))
        };

        request.Headers.Add("X-Notify-Secret", _settings.Geheimnis);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
