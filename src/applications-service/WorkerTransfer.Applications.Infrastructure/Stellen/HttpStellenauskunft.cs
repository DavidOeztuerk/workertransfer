using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Jobs;

/// <summary>Wo der Jobs-Dienst antwortet.</summary>
public sealed class JobSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Jobs";

    /// <summary>Die Basisadresse des Jobs-Dienstes.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange ein Aufruf dauern darf.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Was <c>GET /jobs/{id}</c> davon zurückgibt, was hier gebraucht wird.</summary>
internal sealed record JobResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("location")] string? Location = null,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null);

/// <summary>Fragt den Jobs-Dienst.</summary>
/// <remarks>
/// Über <c>GET /jobs/{id}</c> — den Endpunkt, der bewusst ohne Anmeldung
/// auskommt und Entwurf, geschlossen und nicht vorhanden ununterscheidbar
/// hält.
/// </remarks>
public sealed class HttpJobLookup(
    IHttpClientFactory fabrik,
    IOptions<JobSettings> einstellungen,
    ILogger<HttpJobLookup> protokoll) : IJobLookup
{
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "jobs";

    private readonly JobSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task<PublicJob?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new JobLookupSilent("Jobs:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(
                new Uri(new Uri(_einstellungen.Adresse), $"/jobs/{job}"), cancellationToken);
        }
        catch (HttpRequestException error)
        {
            protokoll.LogWarning("Jobs-Dienst nicht erreichbar ({Art})", error.GetType().Name);
            throw new JobLookupSilent("jobs-service ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Jobs-Dienst antwortete nicht rechtzeitig");
            throw new JobLookupSilent("jobs-service antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            // Der einzige Statuscode, der eine Aussage über die Job ist.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Nicht als „gibt es nicht" behandeln: die Person bekäme eine
                // Absage, die niemand ausgesprochen hat.
                throw new JobLookupSilent(
                    $"jobs-service antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                var gelesen = await response.Content
                    .ReadFromJsonAsync<JobResponse>(cancellationToken)
                    ?? throw new JobLookupSilent("jobs-service sandte nichts.");

                return new PublicJob(
                    gelesen.Id,
                    new TenantId(gelesen.TenantId),
                    gelesen.Title,
                    gelesen.Description ?? string.Empty,
                    gelesen.Location ?? string.Empty,
                    gelesen.Skills ?? []);
            }
            catch (JsonException error)
            {
                throw new JobLookupSilent(
                    "jobs-service sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
