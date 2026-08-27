using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Einwilligung;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class Einwilligungseinstellungen
{
    /// <summary>Der Abschnitt, an den das gebunden wird.</summary>
    public const string Abschnitt = "Consent";

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
public sealed class HttpEinwilligungstor(
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<Einwilligungseinstellungen> einstellungen,
    ILogger<HttpEinwilligungstor> protokoll) : IEinwilligungstor
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly Einwilligungseinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<bool> DarfGezeigtWerdenAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new EinwilligungSchweigt("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), "/consent/check"))
        {
            Content = JsonContent.Create(
                new EinwilligungsfrageV1(wer.Value, Einwilligungsschluessel.Sichtbarkeit),
                options: Format)
        };

        if (token.Wert is { Length: > 0 } wert)
        {
            anfrage.Headers.Add("Authorization", $"Bearer {wert}");
        }

        HttpResponseMessage antwort;

        try
        {
            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning("Consent-Ledger nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new EinwilligungSchweigt("consent-service ist nicht erreichbar.", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new EinwilligungSchweigt("consent-service antwortete nicht rechtzeitig.", fehler);
        }

        using (antwort)
        {
            if (!antwort.IsSuccessStatusCode)
            {
                // 401 und 403 eingeschlossen: unser Token taugt nicht — ein
                // Systemproblem, keine Aussage über die Einwilligung.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)antwort.StatusCode);

                throw new EinwilligungSchweigt(
                    $"consent-service antwortete mit {(int)antwort.StatusCode}.");
            }

            try
            {
                var gelesen = await antwort.Content
                    .ReadFromJsonAsync<EinwilligungsantwortV1>(Format, cancellationToken)
                    ?? throw new EinwilligungSchweigt("consent-service sandte nichts.");

                // Erteilt und nicht gelöscht. Alles andere ist ein Nein.
                return gelesen.Granted && !gelesen.Deleted;
            }
            catch (JsonException fehler)
            {
                throw new EinwilligungSchweigt(
                    "consent-service sandte eine unbrauchbare Antwort.", fehler);
            }
        }
    }
}
