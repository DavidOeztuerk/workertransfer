using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Consent;

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
    /// <remarks>
    /// Short on purpose. A silent ledger has to become a 503 quickly; hanging
    /// on it would turn a slow dependency into a service that answers nothing
    /// at all.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Grant(Guid SubjectId, string Capability);

/// <summary>What a revocation looks like on the wire.</summary>
/// <remarks>
/// The ledger demands a reason for a revocation and none for a grant: taking
/// something away has to be explicable, giving it does not.
/// </remarks>
internal sealed record Revocation(Guid SubjectId, string Capability, string Reason);

/// <summary>Asks the ledger, and writes into it.</summary>
/// <remarks>
/// Synchronously and without a cache (ADR-0013). There is no field here that
/// could hold an answer, and no <c>IMemoryCache</c> in the constructor: with
/// this statement a cache would be no performance detail but a broken promise.
/// <para>
/// Every call travels with <em>the caller's</em> token rather than with a
/// service account, so the ledger's log records who really asked.
/// </para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "consent";

    /// <summary>Snake_case in both directions — the form the ledger speaks.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Reason = "access to the market status withdrawn";

    private const string RouteReason = "route to the employer closed";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public Task<bool> MaySeeProfileAsync(
        SubjectId subject, CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Profile, cancellationToken);

    /// <inheritdoc />
    public Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.Market(company), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        return EachGrantedAsync(
            [.. pairs.Select(pair => (pair.Subject, ConsentKey.Market(pair.Company)))],
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> EmployerRouteOpenAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default) =>
        GrantedAsync(subject, ConsentKey.EmployerRoute(employer), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<bool>> EmployerRoutesOpenAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Employer)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        return EachGrantedAsync(
            [.. pairs.Select(pair => (pair.Subject, ConsentKey.EmployerRoute(pair.Employer)))],
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task OpenEmployerRouteAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default) =>
        await AskAsync<Grant, JsonElement>(
            "/consent/grant",
            new Grant(subject.Value, ConsentKey.EmployerRoute(employer)),
            cancellationToken);

    /// <inheritdoc />
    public async Task CloseEmployerRouteAsync(
        SubjectId subject, TenantId employer, CancellationToken cancellationToken = default) =>
        await AskAsync<Revocation, JsonElement>(
            "/consent/revoke",
            new Revocation(subject.Value, ConsentKey.EmployerRoute(employer), RouteReason),
            cancellationToken);

    private async Task<IReadOnlyList<bool>> EachGrantedAsync(
        IReadOnlyList<(SubjectId Subject, string Capability)> questions,
        CancellationToken cancellationToken)
    {
        var verdicts = new List<bool>(questions.Count);

        // Chunked, because the ledger caps a batch question. Several round
        // trips are still one per hundred instead of one per row.
        foreach (var batch in questions.Chunk(ConsentLimits.MaxBatchSize))
        {
            var question = new ConsentBatchQuestionV1(
                [.. batch.Select(pair => new ConsentQuestionV1(pair.Subject.Value, pair.Capability))]);

            var response = await AskAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>("/consent/check-batch", question, cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused instead of guessed at: aligning it
            // wrongly would show the wrong person's status.
            if (response.Results.Count != batch.Length)
            {
                throw new ConsentGateSilent(
                    $"The ledger answered {batch.Length} questions with "
                    + $"{response.Results.Count} answers.");
            }

            verdicts.AddRange(response.Results.Select(Holds));
        }

        return verdicts;
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        await AskAsync<Grant, JsonElement>(
            "/consent/grant",
            new Grant(subject.Value, ConsentKey.Market(company)),
            cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        await AskAsync<Revocation, JsonElement>(
            "/consent/revoke",
            new Revocation(subject.Value, ConsentKey.Market(company), Reason),
            cancellationToken);

    /// <summary>Granted and not deleted. Everything else is a no.</summary>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<bool> GrantedAsync(
        SubjectId subject, string capability, CancellationToken cancellationToken)
    {
        var response = await AskAsync<ConsentQuestionV1, ConsentResponseV1>(
            "/consent/check",
            new ConsentQuestionV1(subject.Value, capability),
            cancellationToken);

        return Holds(response);
    }

    private async Task<TResponse> AskAsync<TQuestion, TResponse>(
        string path, TQuestion body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            throw new ConsentGateSilent("Consent:BaseUrl is not configured.");
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
                // problem, and no statement about whether the person
                // consented.
                log.LogWarning(
                    "Consent ledger answered with {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service answered with {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(
                           Format, cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sent nothing.");
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sent an unusable answer.", error);
            }
        }
    }
}
