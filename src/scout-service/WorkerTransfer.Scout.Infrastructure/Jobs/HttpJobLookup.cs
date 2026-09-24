using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hits;

namespace WorkerTransfer.Scout.Infrastructure.Jobs;

/// <summary>Where the adverts lie.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Jobs__BaseUrl</c>, <c>Jobs__Timeout</c>); renaming one alone leaves its
/// value on the default, silently.
/// </remarks>
public sealed class JobSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Jobs";

    /// <summary>The base address of jobs-service.</summary>
    /// <remarks>
    /// Stands in the configuration because the egress boundary derives its
    /// allowed hosts from there (<c>ServiceFoundation.CalledHosts</c>). A
    /// destination that stands only in the source is refused — without a log
    /// line.
    /// </remarks>
    public string BaseUrl { get; set; } = "http://jobs-service:8006";

    /// <summary>How long to wait for an answer.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks jobs-service what an advert says about getting there.</summary>
/// <remarks>
/// <para><strong>The public route, no secret.</strong> A published advert is a
/// statement of the company to everybody; <c>GET /jobs/{id}</c> answers it
/// without signing in. An internal door of its own for it would be a second
/// truth about the same question.</para>
///
/// <para>Three fields are read: location, postal code and the remote mode.
/// <strong>Not</strong> title, description or skills — what this service does
/// not get, it cannot pass on (ADR-0004).</para>
/// </remarks>
public sealed class HttpJobLookup(
    IHttpClientFactory factory,
    IHttpContextAccessor accessor,
    IOptions<JobSettings> settings,
    ILogger<HttpJobLookup> log) : IJobLookup
{
    /// <summary>The name the client is registered under.</summary>
    public const string Client = "jobs";

    private readonly JobSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<JobStatement?> GetAsync(
        Guid job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new JobLookupSilent("Jobs:BaseUrl is not set.");
        }

        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.BaseUrl), $"/jobs/{job}"));

        // On the caller's behalf: an advert in draft is seen only by its own
        // company, and exactly the same decision is to fall here.
        if (CallerToken() is { Length: > 0 } token)
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
            // The kind, never the content.
            log.LogWarning(
                "jobs-service is unreachable: {Kind}", error.GetType().Name);

            throw new JobLookupSilent("jobs-service unreachable");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                // It does not exist, or not for this caller. Both mean here: no
                // statement — and that becomes a dash, not a cross.
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "jobs-service answered with {Status}.", (int)response.StatusCode);

                throw new JobLookupSilent($"jobs-service returned {(int)response.StatusCode}");
            }

            try
            {
                var advert = await response.Content
                    .ReadFromJsonAsync<ForeignJobV1>(cancellationToken);

                return advert is null
                    ? throw new JobLookupSilent("jobs-service sent an empty answer")
                    : new JobStatement(
                        advert.Location ?? string.Empty,
                        advert.PostalCode ?? string.Empty,
                        AvailabilityTerms.ReadAttendance(advert.RemoteMode));
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                log.LogWarning("jobs-service answered unintelligibly.");

                throw new JobLookupSilent("jobs-service sent an unusable answer");
            }
        }
    }

    /// <summary>The caller's token: the header first, then the cookie.</summary>
    private string? CallerToken()
    {
        var context = accessor.HttpContext;

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
