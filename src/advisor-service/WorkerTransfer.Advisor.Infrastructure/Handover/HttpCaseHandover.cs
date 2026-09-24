using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Infrastructure.Security;

namespace WorkerTransfer.Advisor.Infrastructure.Handover;

/// <summary>Where transfer-service answers.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Transfer__BaseUrl</c>,
/// <c>Transfer__Timeout</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class HandoverSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Transfer";

    /// <summary>The base address of transfer-service.</summary>
    /// <remarks>
    /// In the configuration, because the egress boundary derives its allowed
    /// hosts from there. Without this line the handover is refused — without a
    /// log line, and the failure would look like one of transfer-service.
    /// </remarks>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>What transfer-service returns on creation — only the id matters.</summary>
internal sealed record CaseResponseV1([property: JsonPropertyName("id")] Guid Id);

/// <summary>What goes out to show interest.</summary>
/// <remarks>
/// A <em>typed</em> contract and not an anonymous object. <c>new { subjectId =
/// … }</c> compiles, serialises and arrives as <c>Guid.Empty</c> at a receiver
/// that declares <c>[JsonPropertyName("subject_id")]</c> — measured on four
/// notification hops that never arrived because of it.
/// </remarks>
internal sealed record InterestV1(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("message")] string Message);

/// <summary>Turns an agreement into a case.</summary>
/// <remarks>
/// <para><strong>A call to the existing door, and no replica.</strong> The
/// triangle consensus lives in transfer-service: whether the market status is
/// released to this company, whether the person is approachable, whether the
/// current employer's release is needed. This adapter knows none of those
/// rules, and that is the point — a second place that knows them would be the
/// first to go stale.</para>
///
/// <para><strong>With the caller's token</strong>, and the caller is the
/// company. A case begins with a company showing interest; it therefore cannot
/// come about without that company's token at all, and a service account here
/// would mean this service could create cases in companies' names.</para>
/// </remarks>
public sealed class HttpCaseHandover(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<HandoverSettings> settings,
    ILogger<HttpCaseHandover> logger) : ICaseHandover
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "transfer";

    private readonly HandoverSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<Guid> HandOverAsync(
        SubjectId subject, string occasion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new LookupSilent("Transfer:BaseUrl is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), "/transfers/"))
        {
            Content = JsonContent.Create(new InterestV1(subject.Value, occasion ?? string.Empty))
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
            logger.LogWarning(
                "transfer-service is unreachable: {Kind}", error.GetType().Name);

            throw new LookupSilent("transfer-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("transfer-service did not answer in time.");

            throw new LookupSilent("transfer-service timed out");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var read = await response.Content
                        .ReadFromJsonAsync<CaseResponseV1>(cancellationToken);

                    return read?.Id
                           ?? throw new LookupSilent("transfer-service sent no id");
                }
                catch (JsonException)
                {
                    throw new LookupSilent("transfer-service sent an unusable answer");
                }
            }

            // 4xx means: transfer-service DID answer, and it refused. That is a
            // statement about the conditions of this case — say a market status
            // that is not released — and not a fault. 5xx is the opposite, and
            // the two must not get the same answer: "cannot" and "we do not
            // know" are two different things.
            if ((int)response.StatusCode is >= 400 and < 500
                && response.StatusCode != HttpStatusCode.RequestTimeout)
            {
                logger.LogInformation(
                    "transfer-service refused the handover: {Status}",
                    (int)response.StatusCode);

                throw new HandoverRefused(
                    $"transfer-service refused with {(int)response.StatusCode}");
            }

            logger.LogWarning(
                "transfer-service answered with {Status}.", (int)response.StatusCode);

            throw new LookupSilent($"transfer-service returned {(int)response.StatusCode}");
        }
    }
}
