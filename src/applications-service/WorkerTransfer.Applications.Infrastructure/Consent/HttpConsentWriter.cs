using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Applications.Application.Ports;

namespace WorkerTransfer.Applications.Infrastructure.Consent;

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Grant(Guid SubjectId, string Capability);

/// <summary>What a revocation looks like.</summary>
/// <remarks>
/// The ledger demands a reason for a revocation and none for a grant: taking
/// something away must be explicable, giving it need not. This one says honestly
/// what triggered the revocation instead of inventing one.
/// </remarks>
internal sealed record Revocation(Guid SubjectId, string Capability, string Reason);

/// <summary>Writes to the ledger.</summary>
/// <remarks>
/// Every call travels with the <em>caller's</em> token instead of a service
/// account, so the ledger's log records who really asked. A foreign actor is
/// turned away there — which is right: the person grants the release, not this
/// service.
/// </remarks>
public sealed class HttpConsentWriter(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentWriter> log) : IConsentWriter
{
    /// <summary>The name under which the client is registered.</summary>
    public const string ClientName = "consent";

    /// <summary>snake_case in both directions — the form the ledger speaks.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Reason = "application withdrawn";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var capability in capabilities)
        {
            await WriteAsync(
                "/consent/grant", new Grant(subject.Value, capability), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        foreach (var capability in capabilities)
        {
            await WriteAsync(
                "/consent/revoke",
                new Revocation(subject.Value, capability, Reason),
                cancellationToken);
        }
    }

    private async Task WriteAsync<TBody>(
        string path,
        TBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new ConsentGateSilent("Consent:BaseUrl is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), path))
        {
            Content = JsonContent.Create(body, options: Format)
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
        catch (HttpRequestException error)
        {
            log.LogWarning("Consent ledger unreachable ({Kind})", error.GetType().Name);
            throw new ConsentGateSilent("consent-service is not reachable.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("Consent ledger did not answer in time");
            throw new ConsentGateSilent("consent-service did not answer in time.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 and 403 included: our token is no good — a system problem,
                // and no statement about whether the person consented.
                log.LogWarning(
                    "Consent ledger answered with {Status}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service answered with {(int)response.StatusCode}.");
            }
        }
    }
}
