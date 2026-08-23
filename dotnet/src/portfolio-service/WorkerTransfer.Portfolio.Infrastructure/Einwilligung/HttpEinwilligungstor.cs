using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Infrastructure.Einwilligung;

/// <summary>Wo der Ledger steht.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt in der Konfiguration.</summary>
    public const string Abschnitt = "Consent";

    /// <summary>Die Basisadresse von consent-service.</summary>
    public string Adresse { get; set; } = string.Empty;
}

/// <summary>Fragt den Ledger — synchron, jedes Mal, ohne Zwischenspeicher.</summary>
/// <remarks>
/// Die Fähigkeit ist <c>portfolio.visibility:public</c>, und sie deckt die
/// Anhänge mit ab (ADR-0021). Ein zweites Tor für Dateien wäre eine zweite
/// Wahrheit, und ein Widerruf wirkte dann auf die Beschreibung, aber nicht auf
/// die Arbeitsprobe.
/// </remarks>
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<Einwilligungseinstellungen> einstellungen) : IEinwilligungstor
{
    /// <summary>Die eine Fähigkeit, um die es hier geht.</summary>
    public const string Faehigkeit = "portfolio.visibility:public";

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> DarfSehenAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            // Kein Ledger eingerichtet heißt nicht „alles frei". Es heißt, dass
            // dieser Dienst die Frage nicht beantworten kann.
            throw new EinwilligungSchweigt("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(nameof(HttpEinwilligungstor));
        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/consent/check"))
        {
            Content = JsonContent.Create(new EinwilligungsfrageV1(wer.Value, Faehigkeit))
        };

        // Im Namen des Aufrufers, nicht mit einem Dienstkonto: das Protokoll des
        // Ledgers soll festhalten, wer wirklich gefragt hat.
        if (Aufrufertoken() is { } token)
        {
            anfrage.Headers.Authorization = new("Bearer", token);
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (Exception fehler) when (fehler is HttpRequestException or TaskCanceledException)
        {
            // Die Art, nie der Inhalt — und ausdrücklich kein `false`: „ich
            // weiß es nicht" ist etwas anderes als „nein".
            throw new EinwilligungSchweigt(
                $"Der Consent-Ledger antwortet nicht ({fehler.GetType().Name}).");
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                throw new EinwilligungSchweigt(
                    $"Der Consent-Ledger antwortet mit {(int)antwort.StatusCode}.");
            }

            using var gelesen = JsonDocument.Parse(
                await antwort.Content.ReadAsStringAsync(cancellationToken));

            return gelesen.RootElement.TryGetProperty("granted", out var erteilt)
                   && erteilt.GetBoolean();
        }
    }

    private string? Aufrufertoken()
    {
        var anfrage = zugriff.HttpContext?.Request;

        if (anfrage is null)
        {
            return null;
        }

        var kopf = anfrage.Headers.Authorization.ToString();

        return kopf.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? kopf["Bearer ".Length..]
            : anfrage.Cookies.TryGetValue("access", out var cookie) && cookie.Length > 0
                ? cookie
                : null;
    }
}
