using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Consent;

/// <summary>What a grant looks like on the wire.</summary>
/// <remarks>
/// Serialised with snake_case naming, so the property names are the wire names
/// <c>subject_id</c> and <c>capability</c>.
/// </remarks>
internal sealed record GrantBody(Guid SubjectId, string Capability);

/// <summary>What a withdrawal looks like on the wire.</summary>
/// <remarks>
/// The ledger requires a reason for a withdrawal and none for a grant: taking
/// something away has to be explicable, giving it does not. The default says
/// honestly where the withdrawal was triggered instead of inventing one.
/// </remarks>
internal sealed record RevocationBody(Guid SubjectId, string Capability, string Reason);

/// <summary>Asks the ledger, and writes to it.</summary>
/// <remarks>
/// Synchronous and without a cache (ADR-0013). There is no field here that
/// could hold an answer and no <c>IMemoryCache</c> in the constructor, and that
/// is deliberate: a withdrawal has to take effect on the very next read, so a
/// cache would not be a performance detail but a broken promise.
/// <para>
/// Every call travels with the <em>caller's</em> token rather than a service
/// account, so the ledger's own log records who really asked.
/// </para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string Client = "consent";

    /// <summary>Snake_case in both directions — the shape the ledger speaks.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Reason = "release of the résumé withdrawn";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public Task<bool> MaySeeProfileAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Profile, cancellationToken);

    /// <inheritdoc />
    public Task<bool> MayReadResumeAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.CurriculumVitae(company), cancellationToken);

    /// <inheritdoc />
    public Task<bool> MaySeeDocumentsAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Documents(company), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MayEachReadResumeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        var verdicts = new List<bool>(pairs.Count);

        // Chunked, because the ledger caps a batch and a person may hold more
        // releases than that. Several round trips are still one per hundred
        // instead of one per row.
        foreach (var chunk in pairs.Chunk(ConsentLimits.MaxBatchSize))
        {
            var question = new ConsentBatchQuestionV1(
                [.. chunk.Select(pair => new ConsentQuestionV1(
                    pair.Subject.Value, ConsentKey.CurriculumVitae(pair.Company)))]);

            var response = await AskAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>("/consent/check-batch", question, cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused rather than guessed at: misaligning
            // them would show the wrong person's release.
            if (response.Results.Count != chunk.Length)
            {
                throw new ConsentGateSilent(
                    $"the ledger answered {chunk.Length} questions with "
                    + $"{response.Results.Count} answers");
            }

            verdicts.AddRange(response.Results.Select(Holds));
        }

        return verdicts;
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        await AskAsync<GrantBody, JsonElement>(
            "/consent/grant",
            new GrantBody(subject.Value, ConsentKey.CurriculumVitae(company)),
            cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        await AskAsync<RevocationBody, JsonElement>(
            "/consent/revoke",
            new RevocationBody(subject.Value, ConsentKey.CurriculumVitae(company), Reason),
            cancellationToken);

    /// <summary>Granted and not erased. Anything else is a no.</summary>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<bool> GrantedAsync(
        SubjectId subject,
        string capability,
        CancellationToken cancellationToken)
    {
        var response = await AskAsync<ConsentQuestionV1, ConsentResponseV1>(
            "/consent/check",
            new ConsentQuestionV1(subject.Value, capability),
            cancellationToken);

        return Holds(response);
    }

    private async Task<TResponse> AskAsync<TQuestion, TResponse>(
        string path,
        TQuestion body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new ConsentGateSilent("Consent:Adresse is not configured");
        }

        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.Adresse), path))
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
            throw new ConsentGateSilent("consent-service is unreachable", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("Consent ledger did not answer in time");
            throw new ConsentGateSilent("consent-service did not answer in time", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 and 403 included: our token is no good — a system
                // problem, and not a statement about whether the person
                // consented.
                log.LogWarning(
                    "Consent ledger answered with {Status}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service answered with {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(
                           Format, cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sent nothing");
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sent an unusable answer", error);
            }
        }
    }
}
