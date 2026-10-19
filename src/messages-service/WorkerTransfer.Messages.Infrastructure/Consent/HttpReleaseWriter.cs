using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Infrastructure.Security;

namespace WorkerTransfer.Messages.Infrastructure.Consent;

/// <summary>What <c>POST /consent/grant</c> reads.</summary>
/// <remarks>
/// A typed body, never an anonymous object: <c>new { subjectId = … }</c>
/// arrives as <c>Guid.Empty</c> at a receiver that reads <c>subject_id</c>.
/// </remarks>
public sealed record GrantV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>Writes the profile release with the caller's — the person's — token.</summary>
public sealed class HttpReleaseWriter(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpReleaseWriter> log) : IReleaseWriter
{
    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task GrantProfileAsync(SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new ConsentGateSilent("Consent:BaseUrl is not configured.");
        }

        using var client = factory.CreateClient(HttpConsentGate.ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), "/consent/grant"))
        {
            Content = JsonContent.Create(new GrantV1(subject.Value, Visibilities.Profile(company)))
        };

        if (token.Value is { Length: > 0 } value)
        {
            request.Headers.Add("Authorization", $"Bearer {value}");
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            log.LogWarning("The consent ledger is unreachable: {Kind}", error.GetType().Name);
            throw new ConsentGateSilent("consent-service unreachable");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // Our token is no good, or the ledger refuses: a system problem,
                // no statement about whether the person consented.
                log.LogWarning("The consent ledger answered with {Status}", (int)response.StatusCode);
                throw new ConsentGateSilent($"consent-service answered with {(int)response.StatusCode}");
            }
        }
    }
}
