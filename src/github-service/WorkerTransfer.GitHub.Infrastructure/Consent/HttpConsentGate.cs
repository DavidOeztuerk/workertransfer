using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.GitHub.Application.Ports;

namespace WorkerTransfer.GitHub.Infrastructure.Consent;

/// <summary>Where the consent ledger answers.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Consent__BaseUrl</c>,
/// <c>Consent__Timeout</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long a question may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks the ledger. Never writes into it.</summary>
/// <remarks>
/// Synchronously and without a cache (ADR-0013). Every call travels with
/// <em>the caller's</em> token, so the ledger's log records who really asked.
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "consent";

    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> MayBeShownAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new ConsentGateSilent("Consent:BaseUrl is not configured.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), "/consent/check"))
        {
            Content = JsonContent.Create(
                new ConsentQuestionV1(subject.Value, ConsentKey.Visibility),
                options: Format)
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
            throw new ConsentGateSilent("consent-service is unreachable.", error);
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
                // 401 and 403 included: our token is no good — a system
                // problem, no statement about the consent.
                log.LogWarning(
                    "Consent ledger answered with {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service answered with {(int)response.StatusCode}.");
            }

            try
            {
                var read = await response.Content
                    .ReadFromJsonAsync<ConsentResponseV1>(Format, cancellationToken)
                    ?? throw new ConsentGateSilent("consent-service sent nothing.");

                // Granted and not deleted. Everything else is a no.
                return read.Granted && !read.Deleted;
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sent an unusable answer.", error);
            }
        }
    }
}
