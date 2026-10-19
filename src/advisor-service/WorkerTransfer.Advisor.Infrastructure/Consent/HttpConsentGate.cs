using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Infrastructure.Security;
using WorkerTransfer.Contracts.Consent;

namespace WorkerTransfer.Advisor.Infrastructure.Consent;

/// <summary>Where the consent ledger can be reached.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Consent__BaseUrl</c>,
/// <c>Consent__Timeout</c>); renaming one alone leaves its value on the default,
/// silently.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Consent";

    /// <summary>The base address of the ledger.</summary>
    /// <remarks>
    /// In the compose network the service name, not <c>localhost</c> — the call
    /// runs from container to container. And it stands in the configuration,
    /// because the egress boundary derives its allowed hosts from there
    /// (<c>ServiceFoundation.CalledHosts</c>): a destination that stood only in
    /// the source would be refused — without a log line.
    /// </remarks>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds. WITHOUT this line <c>HttpClient</c>'s default applies: a
    /// HUNDRED seconds — that is not a timeout but a hanging call that ends some
    /// time, and it hangs the caller along with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>What a grant looks like on the wire.</summary>
internal sealed record Grant(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability);

/// <summary>What a revocation looks like.</summary>
/// <remarks>
/// The ledger demands a reason for a revocation and none for a grant: taking
/// something away must be explainable, giving it need not be.
/// </remarks>
internal sealed record Revocation(
    [property: JsonPropertyName("subject_id")] Guid SubjectId,
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// The ledger — read and written, both synchronously and without a cache.
/// </summary>
/// <remarks>
/// <para>There is no field here that could hold an answer, and no
/// <c>IMemoryCache</c> in the constructor. In a service whose whole purpose is
/// staged visibility, a cache would not be a performance detail but the breach
/// of the promise: a revocation has to take effect on the next request
/// (ADR-0013).</para>
///
/// <para><strong>Whose subject is asked about or written never stands in this
/// service's body — it stands in the token.</strong> Grant and revoke send the
/// id from the caller's verified token; the ledger compares it itself and
/// answers 403 "a consent belongs to its subject" when it does not match. That
/// is why a stage release can only succeed when the person triggers it.</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    ICallerToken token,
    ICurrentPrincipal actor,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> logger) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "consent";

    /// <summary>
    /// How many pairs one round carries.
    /// </summary>
    /// <remarks>
    /// Derived, not chosen: four capabilities are asked per pair, and the ledger
    /// caps a batch question at a hundred pairs. Four times twenty-five is a
    /// hundred.
    /// </remarks>
    public const int PairsPerRound = ConsentLimits.MaxBatchSize / QuestionsPerPair;

    /// <summary>The capabilities asked per pair — see <see cref="Capabilities"/>.</summary>
    private const int QuestionsPerPair = 4;

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Stage>> StagesAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count == 0)
        {
            return [];
        }

        var stages = new List<Stage>(pairs.Count);

        // Chunked, because the ledger caps a batch question. Several round
        // trips are still one per twenty-five rows instead of four per row.
        foreach (var batch in pairs.Chunk(PairsPerRound))
        {
            var questions = batch
                .SelectMany(pair => Capabilities(pair.Company)
                    .Select(capability => new ConsentQuestionV1(pair.Subject.Value, capability)))
                .ToArray();

            var response = await SendAsync<ConsentBatchQuestionV1,
                ConsentBatchResponseV1>(
                "/consent/check-batch",
                new ConsentBatchQuestionV1(questions),
                cancellationToken);

            // The order is part of the contract, and so is the length. A
            // mismatched answer is refused rather than guessed at: assigning it
            // wrongly would mean reading the wrong human being's release.
            if (response.Results.Count != questions.Length)
            {
                throw new ConsentGateSilent(
                    $"{response.Results.Count} answers to {questions.Length} questions");
            }

            for (var i = 0; i < batch.Length; i++)
            {
                stages.Add(Read(response.Results, i * QuestionsPerPair));
            }
        }

        return stages;
    }

    /// <inheritdoc />
    public async Task GrantAsync(
        IReadOnlyList<string> capabilities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var subject = Self();

        foreach (var capability in capabilities)
        {
            await SendAsync<Grant, JsonElement>(
                "/consent/grant", new Grant(subject, capability), cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        IReadOnlyList<string> capabilities,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var subject = Self();

        foreach (var capability in capabilities)
        {
            await SendAsync<Revocation, JsonElement>(
                "/consent/revoke", new Revocation(subject, capability, reason), cancellationToken);
        }
    }

    /// <summary>The four questions per pair, in a fixed order.</summary>
    /// <remarks>
    /// The order is the contract between this method and <see cref="Read"/>;
    /// changing it without counting there would mean reading the answer to
    /// "CV" as "profile".
    /// </remarks>
    private static string[] Capabilities(TenantId company) =>
    [
        StageCapabilities.Profile(company),
        StageCapabilities.ProfilePublic,
        StageCapabilities.Resume(company),
        StageCapabilities.CivilName(company)
    ];

    /// <summary>One stage out of four answers — cumulatively.</summary>
    /// <remarks>
    /// A stage is a <em>state</em> and not a jump: whoever took stage 1 back
    /// does not stand at 3, even if the capability of stage 3 still stands. The
    /// CV visible and the profile not would be a permission without a
    /// foundation.
    /// </remarks>
    private static Stage Read(IReadOnlyList<ConsentResponseV1> responses, int from)
    {
        var profile = Holds(responses[from]) || Holds(responses[from + 1]);
        var resume = Holds(responses[from + 2]);
        var civilName = Holds(responses[from + 3]);

        if (!profile)
        {
            return Stage.None;
        }

        if (!resume)
        {
            return Stage.Profile;
        }

        return civilName ? Stage.Person : Stage.Documents;
    }

    /// <summary>A deleted capability logically withdraws the permission.</summary>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    /// <summary>Who is acting right now — from the verified token, never from the body.</summary>
    private Guid Self() =>
        actor.Current is { } acting
            ? acting.Subject.Value
            : throw new ConsentRefused("not authenticated");

    private async Task<TResponse> SendAsync<TQuestion, TResponse>(
        string path, TQuestion body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            // No ledger configured does not mean "everything released". It
            // means that this service cannot answer the question.
            throw new ConsentGateSilent("Consent:BaseUrl is not set.");
        }

        using var client = factory.CreateClient(ClientName);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(_settings.BaseUrl), path))
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
            logger.LogWarning(
                "The consent ledger is unreachable: {Kind}", error.GetType().Name);

            throw new ConsentGateSilent("consent-service unreachable");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("The consent ledger did not answer in time.");

            throw new ConsentGateSilent("consent-service timed out");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                // The ledger DID answer, and it said no — that is something
                // other than its silence. It happens exactly when somebody
                // wanted to grant for a different human being.
                logger.LogWarning(
                    "The consent ledger refused: {Status}", (int)response.StatusCode);

                throw new ConsentRefused(
                    $"consent-service refused with {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "The consent ledger answered with {Status}.", (int)response.StatusCode);

                throw new ConsentGateSilent(
                    $"consent-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content
                           .ReadFromJsonAsync<TResponse>(cancellationToken)
                       ?? throw new ConsentGateSilent("consent-service sent an empty answer");
            }
            catch (Exception error) when (error is not ConsentGateSilent)
            {
                logger.LogWarning("The consent ledger sent an unreadable answer.");

                throw new ConsentGateSilent("consent-service sent an unusable answer");
            }
        }
    }
}
