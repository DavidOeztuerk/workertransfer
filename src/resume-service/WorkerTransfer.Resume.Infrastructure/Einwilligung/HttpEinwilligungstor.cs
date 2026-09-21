using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Resume.Application.Ports;

namespace WorkerTransfer.Resume.Infrastructure.Einwilligung;

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Erteilung(Guid SubjectId, string Capability);

/// <summary>What a withdrawal looks like on the wire.</summary>
/// <remarks>
/// The ledger requires a reason for a withdrawal and none for a grant: taking
/// something away has to be explicable, giving it does not. The default says
/// honestly where the withdrawal was triggered instead of inventing one.
/// </remarks>
internal sealed record Revocation(Guid SubjectId, string Capability, string Reason);

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
    IHttpClientFactory fabrik,
    IAufrufertoken token,
    IOptions<ConsentSettings> einstellungen,
    ILogger<HttpConsentGate> protokoll) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string Klient = "consent";

    /// <summary>Snake_case in both directions — the shape the ledger speaks.</summary>
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private const string Begruendung = "Freigabe des Lebenslaufs zurückgezogen";

    private readonly ConsentSettings _einstellungen = einstellungen.Value;

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
        GrantedAsync(subject, ConsentKey.Unterlagen(company), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MayEachReadResumeAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        var urteile = new List<bool>(paare.Count);

        // Chunked, because the ledger caps a batch and a person may hold more
        // releases than that. Several round trips are still one per hundred
        // instead of one per row.
        foreach (var stapel in paare.Chunk(ConsentLimits.HoechsteSammelgroesse))
        {
            var frage = new ConsentBatchQuestionV1(
                [.. stapel.Select(paar => new ConsentQuestionV1(
                    paar.Wer.Value, ConsentKey.CurriculumVitae(paar.Firma)))]);

            var response = await AskAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>("/consent/check-batch", frage, cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused rather than guessed at: misaligning
            // them would show the wrong person's release.
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
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        await AskAsync<Erteilung, JsonElement>(
            "/consent/grant",
            new Erteilung(subject.Value, ConsentKey.CurriculumVitae(company)),
            cancellationToken);

    /// <inheritdoc />
    public async Task RevokeAsync(
        SubjectId subject,
        TenantId company,
        CancellationToken cancellationToken = default) =>
        await AskAsync<Revocation, JsonElement>(
            "/consent/revoke",
            new Revocation(subject.Value, ConsentKey.CurriculumVitae(company), Begruendung),
            cancellationToken);

    /// <summary>Granted and not erased. Anything else is a no.</summary>
    private static bool Gilt(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<bool> GrantedAsync(
        SubjectId subject,
        string skill,
        CancellationToken cancellationToken)
    {
        var response = await AskAsync<ConsentQuestionV1, ConsentResponseV1>(
            "/consent/check",
            new ConsentQuestionV1(subject.Value, skill),
            cancellationToken);

        return Gilt(response);
    }

    private async Task<TAntwort> AskAsync<TFrage, TAntwort>(
        string path,
        TFrage body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_einstellungen.Adresse))
        {
            throw new ConsentGateSilent("Consent:Adresse ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(Klient);
        client.Timeout = _einstellungen.Zeitueberschreitung;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_einstellungen.Adresse), path))
        {
            Content = JsonContent.Create(body, options: Format)
        };

        if (token.Wert is { Length: > 0 } value)
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
            protokoll.LogWarning("Consent-Ledger nicht erreichbar ({Art})", error.GetType().Name);
            throw new ConsentGateSilent("consent-service ist nicht erreichbar.", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            protokoll.LogWarning("Consent-Ledger antwortete nicht rechtzeitig");
            throw new ConsentGateSilent("consent-service antwortete nicht rechtzeitig.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 and 403 included: our token is no good — a system
                // problem, and not a statement about whether the person
                // consented.
                protokoll.LogWarning(
                    "Consent-Ledger antwortete mit {Code}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service antwortete mit {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<TAntwort>(
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
