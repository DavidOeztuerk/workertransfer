using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Infrastructure.Einwilligung;

/// <summary>Wo der Ledger steht.</summary>
public sealed class ConsentSettings
{
    /// <summary>Der Section in der Konfiguration.</summary>
    public const string Section = "Consent";

    /// <summary>Die Basisadresse von consent-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fuenf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier.
    /// OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT
    /// Sekunden. Das ist kein Zeitlimit, das ist ein haengender Aufruf mit
    /// einem Ende irgendwann — und er haengt den Aufrufer mit.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt den Ledger — synchron, jedes Mal, ohne Zwischenspeicher.</summary>
/// <remarks>
/// Die Fähigkeit ist <c>portfolio.visibility:public</c>, und sie deckt die
/// Anhänge mit ab (ADR-0021). Ein zweites Tor für Dateien wäre eine zweite
/// Wahrheit, und ein Revocation wirkte dann auf die Beschreibung, aber nicht auf
/// die Arbeitsprobe.
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<ConsentSettings> einstellungen) : IConsentGate
{
    /// <summary>Die eine Fähigkeit, um die es hier geht.</summary>
    public const string Faehigkeit = "portfolio.visibility:public";

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heißt nicht „alles frei". Es heißt, dass
            // dieser Dienst die Frage nicht beantworten kann.
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(nameof(HttpConsentGate));

        client.Timeout = _einstellungen.Zeitueberschreitung;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/consent/check"))
        {
            Content = JsonContent.Create(new ConsentQuestionV1(subject.Value, Faehigkeit))
        };

        // Im Namen des Aufrufers, nicht mit einem Dienstkonto: das Protokoll des
        // Ledgers soll festhalten, wer wirklich gefragt hat.
        if (Aufrufertoken() is { } token)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Die Art, nie der Inhalt — und ausdrücklich kein `false`: „ich
            // weiß es nicht" ist etwas anderes als „nein".
            throw new ConsentGateSilent(
                $"Der Consent-Ledger antwortet nicht ({error.GetType().Name}).");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new ConsentGateSilent(
                    $"Der Consent-Ledger antwortet mit {(int)response.StatusCode}.");
            }

            using var gelesen = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));

            return gelesen.RootElement.TryGetProperty("granted", out var erteilt)
                   && erteilt.GetBoolean();
        }
    }

    private string? Aufrufertoken()
    {
        var request = zugriff.HttpContext?.Request;

        if (request is null)
        {
            return null;
        }

        var header = request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..]
            : request.Cookies.TryGetValue("access", out var cookie) && cookie.Length > 0
                ? cookie
                : null;
    }
}
