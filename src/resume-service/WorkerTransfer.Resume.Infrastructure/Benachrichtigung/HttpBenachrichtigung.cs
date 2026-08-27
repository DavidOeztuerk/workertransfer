using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Resume.Infrastructure.Benachrichtigung;

/// <summary>Where notifications go, and what proves they are ours.</summary>
public sealed class Benachrichtigungseinstellungen
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Notifications";

    /// <summary>The base address of notification-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fuenf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier.
    /// OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT
    /// Sekunden. Das ist kein Zeitlimit, das ist ein haengender Aufruf mit
    /// einem Ende irgendwann — und er haengt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The shared secret. Expressly a different one from the erasure secret.
    /// </summary>
    /// <remarks>
    /// "May trigger a mail" and "may delete everything about a person" must not
    /// be the same piece of paper. Empty means nothing is delivered — and as a
    /// <em>failure</em>, so the row stays due instead of counting as sent.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Hands one intent to notification-service.</summary>
/// <remarks>
/// Throws on a transport error <em>and</em> on a bad status, which is what
/// makes <c>delivered_at</c> mean something. The failure is the outbox's
/// business: it counts the attempt, keeps the row, and tries again.
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
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/notifications"))
        {
            // The whole payload: whom it is about and what kind. No text, no
            // reason, no company name — the recipient reads the current state
            // itself, which is also why a redelivery is harmless.
            Content = JsonContent.Create(new { userId = empfaenger.Value, kind = art })
        };

        anfrage.Headers.Add("X-Notify-Secret", _einstellungen.Geheimnis);

        using var antwort = await client.SendAsync(anfrage, cancellationToken);

        antwort.EnsureSuccessStatusCode();
    }
}
