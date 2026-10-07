using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Infrastructure.Security;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Messages.Infrastructure.Consent;

/// <summary>Where the consent ledger can be reached.</summary>
public sealed class ConsentSettings
{
    /// <summary>The section this binds to.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    /// <remarks>
    /// <para>In the compose network the service name, not <c>localhost</c> — the
    /// call runs from container to container. And it stands in the configuration
    /// because the egress boundary derives its allowed hosts from there
    /// (<c>ServiceFoundation.CalledHosts</c>): a destination that stood only in
    /// the source is refused — without a log line, and the outage would then look
    /// like an outage of the ledger.</para>
    ///
    /// <para>The property name IS the environment key (<c>Consent__BaseUrl</c>);
    /// renaming it alone leaves the value on its default, silently.</para>
    /// </remarks>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds. WITHOUT this line <c>HttpClient</c>'s default applies: a
    /// HUNDRED seconds — that is no time limit but a hanging call with an end
    /// some time, and it hangs the caller with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>The ledger — read, synchronously and without a cache.</summary>
/// <remarks>
/// <para>There is no field here that could hold an answer, and no
/// <c>IMemoryCache</c> in the constructor. A revocation must take effect at the
/// next request (ADR-0013) — and here it decides whether a company may go on
/// writing to a human being.</para>
///
/// <para><strong>Read only.</strong> This service grants no capability and
/// revokes none: it has none of its own, and the others belong to the person
/// (ADR-0047).</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    ICallerToken token,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "consent";

    /// <summary>How many questions are asked per row.</summary>
    /// <remarks>
    /// Two: "for this company" and "for everyone". The second is the wider — whoever
    /// is publicly visible is visible to this company too.
    /// </remarks>
    public const int QuestionsPerRow = 2;

    /// <summary>How many pairs one round carries.</summary>
    /// <remarks>
    /// Derived, not chosen: the ledger caps a batch question at a hundred
    /// questions, and each pair takes two.
    /// </remarks>
    public const int PairsPerRound = ConsentLimits.MaxBatchSize / QuestionsPerRow;

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default) =>
        (await MayEachSeeAsync([(subject, company)], cancellationToken))[0];

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MayEachSeeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            return [];
        }

        var allowed = new List<bool>(pairs.Count);

        // Chunked, because the ledger caps a batch question. Several round trips
        // are still one per fifty rows instead of two per row.
        foreach (var chunk in pairs.Chunk(PairsPerRound))
        {
            var questions = chunk
                .SelectMany(pair => new[]
                {
                    new ConsentQuestionV1(pair.Subject.Value, Visibilities.Profile(pair.Company)),
                    new ConsentQuestionV1(pair.Subject.Value, Visibilities.ProfilePublic)
                })
                .ToArray();

            var response = await SendAsync(
                new ConsentBatchQuestionV1(questions), cancellationToken);

            // The order belongs to the contract, and so does the length. A
            // mismatched answer is refused instead of guessed at: aligning it
            // wrongly would mean reading the wrong human being's release.
            if (response.Results.Count != questions.Length)
            {
                throw new ConsentGateSilent(
                    $"{response.Results.Count} answers to {questions.Length} questions");
            }

            for (var i = 0; i < chunk.Length; i++)
            {
                var start = i * QuestionsPerRow;

                allowed.Add(Holds(response.Results[start]) || Holds(response.Results[start + 1]));
            }
        }

        return allowed;
    }

    /// <summary>A deleted capability withdraws the permission logically.</summary>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<ConsentBatchResponseV1> SendAsync(
        ConsentBatchQuestionV1 body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            // No ledger configured does not mean "everything released". It means
            // this service cannot answer the question.
            throw new ConsentGateSilent("Consent:BaseUrl is not configured.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), "/consent/check-batch"))
        {
            Content = JsonContent.Create(body)
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
            // Only the kind, never the content: the question was about a human being.
            log.LogWarning(
                "The consent ledger is unreachable: {Kind}", error.GetType().Name);

            throw new ConsentGateSilent("consent-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("The consent ledger did not answer in time.");

            throw new ConsentGateSilent("consent-service timed out");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // A refusal is silence here too: it says something about THIS
                // call and nothing about the human being asked about. Translating
                // it into "not released" would turn our error into a statement
                // about them.
                log.LogWarning(
                    "The consent ledger refused: {Status}", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service refused with {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "The consent ledger answered with {Status}.", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content
                           .ReadFromJsonAsync<ConsentBatchResponseV1>(cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sent an empty answer");
            }
            catch (Exception error) when (error is not ConsentGateSilent)
            {
                log.LogWarning("The consent ledger sent an unreadable answer.");

                throw new ConsentGateSilent("consent-service sent an unusable answer");
            }
        }
    }
}
