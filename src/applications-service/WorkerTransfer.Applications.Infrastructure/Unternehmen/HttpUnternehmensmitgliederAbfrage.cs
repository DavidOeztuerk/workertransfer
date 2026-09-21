using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Infrastructure.Unternehmen;

/// <summary>Wo der Identity-Dienst für interne Abfragen antwortet.</summary>
public sealed class CompanyMemberSettings
{
    public const string Section = "Identity";

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
public sealed class HttpCompanyMemberLookup(
    IHttpClientFactory fabrik,
    IOptions<CompanyMemberSettings> einstellungen,
    ILogger<HttpCompanyMemberLookup> protokoll) : ICompanyMemberLookup
{
    private readonly CompanyMemberSettings _einstellungen = einstellungen.Value;

    public const string Klient = "identity-internal";

    private const string Geheimniskopf = "X-Notify-Secret";

    public async Task<IReadOnlyList<SubjectId>> GetAsync(
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new CompanyLookupSilent("Identity:Adresse ist nicht gesetzt.");
        }

        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new CompanyLookupSilent("Identity:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(_einstellungen.Adresse), $"/internal/companies/{company.Value}/members"));
            request.Headers.Add(Geheimniskopf, _einstellungen.Geheimnis);

            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            protokoll.LogWarning("Identity-Dienst (intern) nicht erreichbar ({Art})", error.GetType().Name);
            throw new CompanyLookupSilent("identity-service (intern) ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Identity-Dienst (intern) antwortete nicht rechtzeitig");
            throw new CompanyLookupSilent("identity-service (intern) antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new CompanyLookupSilent("identity-service (intern) nicht erreichbar oder Geheimnis ungültig.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CompanyLookupSilent(
                    $"identity-service (intern) antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                var gelesen = await response.Content
                    .ReadFromJsonAsync<CompanyMembersResponseV1>(cancellationToken)
                    ?? throw new CompanyLookupSilent("identity-service (intern) sandte nichts.");

                return [.. gelesen.Mitglieder.Select(m => new SubjectId(m.SubjectId))];
            }
            catch (JsonException error)
            {
                throw new CompanyLookupSilent(
                    "identity-service (intern) sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
