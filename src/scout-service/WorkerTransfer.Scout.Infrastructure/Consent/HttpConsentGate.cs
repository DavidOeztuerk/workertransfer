using System.Net.Http.Headers;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Scout.Application.Ports;

namespace WorkerTransfer.Scout.Infrastructure.Consent;

/// <summary>Where the consent ledger can be reached.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Consent__BaseUrl</c>, <c>Consent__Timeout</c>); renaming one alone leaves
/// its value on the default, silently.
/// </remarks>
public sealed class ConsentSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Consent";

    /// <summary>The ledger's base address.</summary>
    /// <remarks>
    /// In the compose network the service name, not <c>localhost</c> — the call
    /// runs from container to container. And it stands in the configuration
    /// because the egress boundary derives its allowed hosts from there
    /// (<c>ServiceFoundation.CalledHosts</c>).
    /// </remarks>
    public string BaseUrl { get; set; } = "http://consent-service:8002";

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds. WITHOUT this line <c>HttpClient</c>'s default applies: a
    /// HUNDRED seconds — that is no time limit but a hanging call with an end
    /// some time, and it hangs the caller with it.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks the ledger — synchronously, every time, without a cache.</summary>
/// <remarks>
/// <para>Word for word the same as the one in profile-service, and that is the
/// point: the same question has to get the same answer. Two capabilities per
/// human being (public OR for this company), in the order of the
/// questions.</para>
///
/// <para>Asked with the <em>caller's</em> token and not with an account of its
/// own: the service asks on their behalf, and the ledger's log is to record who
/// really asked. The header first, otherwise the cookie — the UI never sees the
/// <c>httpOnly</c> token and can only give it back that way.</para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    IHttpContextAccessor accessor,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under.</summary>
    public const string Client = "consent";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<bool>> MaySeeAllAsync(
        IReadOnlyList<SubjectId> subjects,
        TenantId company,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);

        if (subjects.Count == 0)
        {
            return [];
        }

        if (string.IsNullOrEmpty(_settings.BaseUrl))
        {
            // No ledger set up does not mean "everything released". It means
            // that this service cannot answer the question.
            throw new ConsentGateSilent("Consent:BaseUrl is not set.");
        }

        var forCompany = Visibilities.ForCompany(company);

        var pairs = subjects
            .SelectMany(subject => new[]
            {
                new ConsentQuestionV1(subject.Value, Visibilities.Public),
                new ConsentQuestionV1(subject.Value, forCompany)
            })
            .ToArray();

        if (pairs.Length > ConsentLimits.MaxBatchSize)
        {
            // The caller asks more than the contract carries. That is a
            // programming error and no information about a human being —
            // therefore loud, not as "not released".
            throw new ConsentGateSilent(
                $"{pairs.Length} pairs exceed "
                + $"{ConsentLimits.MaxBatchSize}");
        }

        var response = await Ask(pairs, cancellationToken);

        if (response.Results.Count != pairs.Length)
        {
            throw new ConsentGateSilent(
                $"{response.Results.Count} answers to {pairs.Length} questions");
        }

        return
        [
            .. Enumerable.Range(0, subjects.Count).Select(index =>
                Holds(response.Results[index * 2]) || Holds(response.Results[(index * 2) + 1]))
        ];
    }

    /// <summary>A deleted capability logically withdraws the permission.</summary>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<ConsentBatchResponseV1> Ask(
        IReadOnlyList<ConsentQuestionV1> pairs, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Timeout;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_settings.BaseUrl), "/consent/check-batch"))
        {
            Content = JsonContent.Create(new ConsentBatchQuestionV1(pairs))
        };

        if (CallerToken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // Only the kind, never the content: what was asked is a list of human
            // beings and does not belong in the log.
            log.LogWarning(
                "The consent ledger is unreachable: {Kind}", error.GetType().Name);

            throw new ConsentGateSilent("consent-service unreachable");
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("The consent ledger did not answer in time.");

            throw new ConsentGateSilent($"consent-service timed out ({error.GetType().Name})");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 401 and 403 too: our token is no good — that is a system
                // problem and no statement about whether the person consented.
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
                log.LogWarning("The consent ledger answered unintelligibly.");

                throw new ConsentGateSilent("consent-service sent an unusable answer");
            }
        }
    }

    /// <summary>The caller's token: the header first, then the cookie.</summary>
    private string? CallerToken()
    {
        var context = accessor.HttpContext;

        if (context is null)
        {
            return null;
        }

        var header = context.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : context.Request.Cookies["access"];
    }
}
