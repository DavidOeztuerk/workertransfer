using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Infrastructure.Security;

namespace WorkerTransfer.Messages.Infrastructure.Process;

/// <summary>Where the person's own facts live.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>OwnFacts__AdvisorUrl</c>,
/// <c>OwnFacts__TransferUrl</c>, <c>OwnFacts__Timeout</c>).
/// </remarks>
public sealed class OwnFactsSettings
{
    /// <summary>The configuration section.</summary>
    public const string Section = "OwnFacts";

    /// <summary>advisor-service, for her mandate.</summary>
    public string AdvisorUrl { get; set; } = string.Empty;

    /// <summary>transfer-service, for her notice period.</summary>
    public string TransferUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for each.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>The part of advisor-service's mandate the advisor reads.</summary>
public sealed record MandateReadV1(
    [property: JsonPropertyName("entry_month")] string? EntryMonth,
    [property: JsonPropertyName("salary_min")] int? SalaryMin,
    [property: JsonPropertyName("salary_max")] int? SalaryMax,
    [property: JsonPropertyName("workload_percent")] int? WorkloadPercent);

/// <summary>The part of transfer-service's notice answer the advisor reads.</summary>
public sealed record NoticeReadV1(
    [property: JsonPropertyName("earliest_start")] DateOnly? EarliestStart);

/// <summary>Fetches her mandate and earliest start — with her own token (ADR-0049).</summary>
/// <remarks>
/// <strong>Her token, so exactly what she may see herself.</strong> A silent
/// service is reported as unknown, never as "none": the advisor says it is
/// missing instead of advising as if she had stated nothing.
/// </remarks>
public sealed class HttpOwnFactsLookup(
    IHttpClientFactory factory,
    IOptions<OwnFactsSettings> settings,
    ICallerToken token,
    ILogger<HttpOwnFactsLookup> log) : IOwnFactsLookup
{
    /// <summary>The client's name.</summary>
    public const string ClientName = "own-facts";

    private readonly OwnFactsSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<OwnFacts> GetAsync(CancellationToken cancellationToken = default)
    {
        var (mandate, mandateKnown) = await ReadAsync<MandateReadV1>(_settings.AdvisorUrl, "/advisor/me/mandate", cancellationToken);
        var (notice, noticeKnown) = await ReadAsync<NoticeReadV1>(_settings.TransferUrl, "/market/me/notice", cancellationToken);

        return new OwnFacts(
            mandate is null ? null : new OwnWishes(mandate.EntryMonth, mandate.SalaryMin, mandate.SalaryMax, mandate.WorkloadPercent),
            !mandateKnown,
            notice?.EarliestStart,
            !noticeKnown);
    }

    /// <returns>The body (or <c>null</c> on 404: none stated) and whether the service answered at all.</returns>
    private async Task<(T? Body, bool Known)> ReadAsync<T>(string baseUrl, string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(baseUrl) || token.Value is not { Length: > 0 } bearer)
        {
            return (null, false);
        }

        try
        {
            using var client = factory.CreateClient(ClientName);
            client.Timeout = _settings.Timeout;

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(baseUrl), path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            using var response = await client.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (null, true);
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("A source of own facts answered with {Status}", (int)response.StatusCode);
                return (null, false);
            }

            return (await response.Content.ReadFromJsonAsync<T>(cancellationToken), true);
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException
                                          || (error is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            log.LogWarning("A source of own facts is unreachable ({Kind})", error.GetType().Name);
            return (null, false);
        }
    }
}
