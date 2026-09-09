using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Infrastructure.Unternehmen;

/// <summary>Wo der Identity-Dienst für interne Abfragen antwortet.</summary>
public sealed class UnternehmensmitgliederEinstellungen
{
    public const string Abschnitt = "Identity";

    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    /// <remarks>
    /// Fünf Sekunden, wie bei jedem anderen Dienst-zu-Dienst-Aufruf hier.
    /// OHNE diese Zeile gilt die Vorgabe von <c>HttpClient</c>: HUNDERT
    /// Sekunden.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Dasselbe Geheimnis wie <c>Notifications:Geheimnis</c>.</summary>
    public string Geheimnis { get; set; } = string.Empty;
}

/// <summary>Fragt den Identity-Dienst über den internen Draht.</summary>
/// <remarks>
/// Ruft <c>GET /internal/companies/{tenantId}/members</c> auf.
/// Benutzt denselben gemeinsamen Geheimniskopf wie der Benachrichtigungsweg.
/// Liest den geteilten Vertrag — nie ein anonymes Objekt, nie einen zweiten
/// Typen mit denselben Feldern.
/// </remarks>
public sealed class HttpUnternehmensmitgliederAbfrage(
    IHttpClientFactory fabrik,
    IOptions<UnternehmensmitgliederEinstellungen> einstellungen,
    ILogger<HttpUnternehmensmitgliederAbfrage> protokoll) : IUnternehmensmitgliederAbfrage
{
    private readonly UnternehmensmitgliederEinstellungen _einstellungen = einstellungen.Value;

    public const string Klient = "identity-internal";

    private const string Geheimniskopf = "X-Notify-Secret";

    public async Task<IReadOnlyList<SubjectId>> HoleAsync(
        TenantId firma,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new FirmaSchweigt("Identity:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new FirmaSchweigt("Identity:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage antwort;

        try
        {
            using var anfrage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_einstellungen.Adresse), $"/internal/companies/{firma.Value}/members"));
            anfrage.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

            antwort = await client.SendAsync(anfrage, cancellationToken);
        }
        catch (HttpRequestException fehler)
        {
            protokoll.LogWarning("Identity-Dienst (intern) nicht erreichbar ({Art})", fehler.GetType().Name);
            throw new FirmaSchweigt("identity-service (intern) ist nicht erreichbar.", fehler);
        }
        catch (TaskCanceledException fehler) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Identity-Dienst (intern) antwortete nicht rechtzeitig");
            throw new FirmaSchweigt("identity-service (intern) antwortete nicht rechtzeitig.", fehler);
        }

        using (antwort)
        {
            if (antwort.StatusCode == HttpStatusCode.NotFound)
            {
                throw new FirmaSchweigt("identity-service (intern) nicht erreichbar oder Geheimnis ungültig.");
            }

            if (!antwort.IsSuccessStatusCode)
            {
                throw new FirmaSchweigt(
                    $"identity-service (intern) antwortete mit {(int)antwort.StatusCode}.");
            }

            try
            {
                var gelesen = await antwort.Content
                    .ReadFromJsonAsync<UnternehmensmitgliederAntwortV1>(cancellationToken)
                    ?? throw new FirmaSchweigt("identity-service (intern) sandte nichts.");

                return [.. gelesen.Mitglieder.Select(m => new SubjectId(m.SubjectId))];
            }
            catch (JsonException fehler)
            {
                throw new FirmaSchweigt(
                    "identity-service (intern) sandte eine unbrauchbare Antwort.", fehler);
            }
        }
    }
}
