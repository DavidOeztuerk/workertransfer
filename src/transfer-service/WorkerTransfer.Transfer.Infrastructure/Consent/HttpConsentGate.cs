using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Infrastructure.Consent;

/// <summary>Wo der Consent-Ledger antwortet.</summary>
public sealed class ConsentSettings
{
    /// <summary>Der Section, an den das gebunden wird.</summary>
    public const string Section = "Consent";

    /// <summary>Die Basisadresse des Ledgers.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>Wie lange eine Frage dauern darf.</summary>
    /// <remarks>
    /// Short on purpose. A silent ledger has to become a 503 quickly; hanging
    /// on it would turn a slow dependency into a service that answers nothing
    /// at all.
    /// </remarks>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Grant(Guid SubjectId, string Capability);

/// <summary>Wie ein Revocation aussieht.</summary>
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
    /// <summary>Der Name, unter dem der Klient registriert ist.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in beide Richtungen — die Form, die der Ledger spricht.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Reason = "Zugriff auf den Marktstatus zurückgezogen";

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
    public async Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        var urteile = new List<bool>(paare.Count);

        // Chunked, because the ledger caps a batch question. Several round
        // trips are still one per hundred instead of one per row.
        foreach (var stapel in paare.Chunk(ConsentLimits.HoechsteSammelgroesse))
        {
            var frage = new ConsentBatchQuestionV1(
                [.. stapel.Select(pair => new ConsentQuestionV1(
                    pair.Subject.Value, ConsentKey.Market(pair.Company)))]);

            var response = await AskAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>("/consent/check-batch", frage, cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused instead of guessed at: aligning it
            // wrongly would show the wrong person's status.
            if (response.Results.Count != stapel.Length)
            {
                throw new ConsentGateSilent(
                    $"Der Ledger beantwortete {stapel.Length} Fragen mit "
                    + $"{response.Results.Count} Antworten.");
            }

            urteile.AddRange(response.Results.Select(Gilt));
        }

        return urteile;
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
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<bool> GrantedAsync(
        SubjectId subject, string skill, CancellationToken cancellationToken)
    {
        var response = await AskAsync<ConsentQuestionV1, ConsentResponseV1>(
            "/consent/check",
            new ConsentQuestionV1(subject.Value, skill),
            cancellationToken);

        return Gilt(response);
    }

    private async Task<TResponse> AskAsync<TFrage, TResponse>(
        string path, TFrage body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = factory.CreateClient(Klient);
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
            log.LogWarning("Consent-Ledger nicht erreichbar ({Art})", error.GetType().Name);
            throw new ConsentGateSilent("consent-service ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new ConsentGateSilent("consent-service antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 and 403 included: our token is no good — a system
                // problem, and no statement about whether the person
                // consented.
                log.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TResponse>(
                           Format, cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sandte nichts.");
            }
            catch (JsonException error)
            {
                throw new ConsentGateSilent(
                    "consent-service sandte eine unbrauchbare Antwort.", error);
            }
        }
    }
}
