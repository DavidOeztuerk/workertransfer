using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class ConsentSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange eine Frage dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt den Ledger. Schreibt nie in ihn.</summary>
/// <remarks>
/// Synchron und ohne Zwischenspeicher (ADR-0013). Jeder Aufruf reist mit dem
/// Token <em>des Aufrufers</em>, damit das Protokoll des Ledgers festhält, wer
/// wirklich gefragt hat.
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> MayBeShownAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/consent/check"))
        {
            Content = JsonContent.Create(
                new ConsentQuestionV1(subject.Value, ConsentKey.Sichtbarkeit),
                options: Format)
        };

        if (token.Wert is { Length: > 0 } value)
        {
            request.Headers.Add("Authorization", $"Bearer {value}");
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            protokoll.LogWarning("Consent-Ledger nicht erreichbar ({Art})", error.GetType().Name);
            throw new ConsentGateSilent("consent-service ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new ConsentGateSilent("consent-service antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 und 403 eingeschlossen: unser Token taugt nicht — ein
                // Systemproblem, keine Aussage über die Einwilligung.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                var gelesen = await response.Content
                    .ReadFromJsonAsync<ConsentResponseV1>(Format, cancellationToken)
                    ?? throw new ConsentGateSilent("consent-service sandte nichts.");

                // Erteilt und nicht gelöscht. Alles andere ist ein Nein.
                return gelesen.Granted && !gelesen.Deleted;
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
