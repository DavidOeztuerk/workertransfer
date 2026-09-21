using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hit;

namespace WorkerTransfer.Scout.Infrastructure.Jobs;

/// <summary>Wo die Anzeigen liegen.</summary>
public sealed class JobSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Jobs";

    /// <summary>Die Basisadresse von jobs-service.</summary>
    /// <remarks>
    /// Steht in der Konfiguration, weil die Egress-Grenze ihre erlaubten Hosts
    /// von dort ableitet (<c>ServiceFoundation.GerufeneHosts</c>). Ein Ziel, das
    /// nur im Quelltext steht, wird abgewiesen — ohne Protokollzeile.
    /// </remarks>
    public string Adresse { get; set; } = "http://jobs-service:8006";

    /// <summary>Wie lange auf eine Antwort gewartet wird.</summary>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Fragt jobs-service nach dem, was eine Anzeige über ihren Weg sagt.</summary>
/// <remarks>
/// <para><strong>Die öffentliche Route, kein Geheimnis.</strong> Eine
/// veröffentlichte Anzeige ist eine Aussage des Unternehmens an alle;
/// <c>GET /jobs/{id}</c> beantwortet sie ohne Anmeldung. Eine eigene interne Tür
/// dafür wäre eine zweite Wahrheit über dieselbe Frage.</para>
///
/// <para>Gelesen werden drei Felder: Ort, Postleitzahl und der Remotegrad.
/// <strong>Nicht</strong> Titel, Beschreibung oder Fähigkeiten — was dieser
/// Dienst nicht bekommt, kann er nicht weiterreichen (ADR-0004).</para>
/// </remarks>
public sealed class HttpJobs(
    IHttpClientFactory fabrik,
    IHttpContextAccessor zugriff,
    IOptions<JobSettings> einstellungen,
    ILogger<HttpJobs> protokoll) : IJobs
{
    /// <summary>Der Name des Mandanten in der Clientfabrik.</summary>
    public const string Client = "jobs";

    private readonly JobSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<JobStatement?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new JobLookupSilent("Jobs:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Client);
        client.Timeout = _einstellungen.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_einstellungen.Adresse), $"/jobs/{job}"));

        // Im Namen des Aufrufers: eine Anzeige im Entwurf sieht nur ihr eigenes
        // Unternehmen, und genau dieselbe Entscheidung soll hier fallen.
        if (Aufrufertoken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // Die Art, nie der Inhalt.
            protokoll.LogWarning(
                "jobs-service ist nicht erreichbar: {Art}", error.GetType().Name);

            throw new JobLookupSilent("jobs-service unreachable");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                // Es gibt sie nicht, oder nicht fuer diesen Aufrufer. Beides
                // heisst hier: keine Aussage — und daraus wird ein Strich, kein
                // Kreuz.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                protokoll.LogWarning(
                    "jobs-service antwortete mit {Status}.", (int)response.StatusCode);

                throw new JobLookupSilent($"jobs-service returned {(int)response.StatusCode}");
            }

            try
            {
                var gelesen = await response.Content
                    .ReadFromJsonAsync<ForeignJobV1>(cancellationToken);

                return gelesen is null
                    ? throw new JobLookupSilent("jobs-service sent an empty answer")
                    : new JobStatement(
                        gelesen.Location ?? string.Empty,
                        gelesen.PostalCode ?? string.Empty,
                        AvailabilityTerms.Anwesenheit(gelesen.RemoteMode));
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                protokoll.LogWarning("jobs-service antwortete unverständlich.");

                throw new JobLookupSilent("jobs-service sent an unusable answer");
            }
        }
    }

    private string? Aufrufertoken()
    {
        var context = zugriff.HttpContext;

        if (context is null)
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : context.Request.Cookies["access"];
    }
}
