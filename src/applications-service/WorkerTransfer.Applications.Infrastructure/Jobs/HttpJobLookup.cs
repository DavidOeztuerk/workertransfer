using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Jobs;

/// <summary>Where jobs-service answers.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Jobs__Adresse</c>, <c>Jobs__Zeitueberschreitung</c>), so they stay German
/// until the keys are renamed everywhere at once — in compose, in the chart and
/// in <c>.env.example</c>.
/// </remarks>
public sealed class JobSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Jobs";

    /// <summary>The base address of jobs-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>How long a call may take.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>What of <c>GET /jobs/{id}</c> is needed here.</summary>
internal sealed record JobResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("location")] string? Location = null,
    [property: JsonPropertyName("skills")] IReadOnlyList<string>? Skills = null);

/// <summary>Asks jobs-service.</summary>
/// <remarks>
/// Via <c>GET /jobs/{id}</c> — the endpoint that deliberately needs no sign-in
/// and keeps draft, closed and missing indistinguishable.
/// </remarks>
public sealed class HttpJobLookup(
    IHttpClientFactory factory,
    IOptions<JobSettings> settings,
    ILogger<HttpJobLookup> log) : IJobLookup
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "jobs";

    private readonly JobSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<PublicJob?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new JobLookupSilent("Jobs:Adresse is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Zeitueberschreitung;

        HttpResponseMessage response;

        try
        {
            response = await client.GetAsync(
                new Uri(new Uri(_settings.Adresse), $"/jobs/{job}"), cancellationToken);
        }
        catch (HttpRequestException error)
        {
            log.LogWarning("jobs-service unreachable ({Kind})", error.GetType().Name);
            throw new JobLookupSilent("jobs-service is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("jobs-service did not answer in time");
            throw new JobLookupSilent("jobs-service did not answer in time.", error);
        }

        using (response)
        {
            // The only status code that is a statement about the job.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // Not to be treated as "does not exist": the person would get a
                // rejection nobody gave.
                throw new JobLookupSilent(
                    $"jobs-service answered with {(int)response.StatusCode}.");
            }

            try
            {
                var read = await response.Content
                    .ReadFromJsonAsync<JobResponse>(cancellationToken)
                    ?? throw new JobLookupSilent("jobs-service sent nothing.");

                return new PublicJob(
                    read.Id,
                    new TenantId(read.TenantId),
                    read.Title,
                    read.Description ?? string.Empty,
                    read.Location ?? string.Empty,
                    read.Skills ?? []);
            }
            catch (JsonException error)
            {
                throw new JobLookupSilent(
                    "jobs-service sent an unusable response.", error);
            }
        }
    }
}
