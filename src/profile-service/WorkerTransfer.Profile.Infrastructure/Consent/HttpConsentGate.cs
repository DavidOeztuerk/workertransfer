using System.Net.Http.Headers;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Consent;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Profile.Infrastructure.Consent;

/// <summary>Asks the consent ledger over HTTP.</summary>
/// <remarks>
/// Synchronously and without a cache (ADR-0013). A revocation must take effect
/// at the next read, not the one after — a cache would be no performance detail
/// here but a breach of the rule.
/// <para>
/// Asked with the <em>caller's</em> token and not with an account of its own:
/// the service asks on the caller's behalf. The header first, otherwise the
/// cookie — the interface never sees the <c>httpOnly</c> token and can give it
/// back only that way.
/// </para>
/// </remarks>
public sealed class HttpConsentGate(
    IHttpClientFactory factory,
    IHttpContextAccessor accessor,
    IOptions<ConsentSettings> settings,
    ILogger<HttpConsentGate> log) : IConsentGate
{
    /// <summary>The name the client is registered under in the client factory.</summary>
    public const string Client = "consent";

    private readonly ConsentSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        // Two questions in one round, although it is about one human being: the
        // public release is the common case, but a second round trip for the
        // other costs more than the second line in the same body.
        //
        // `AskAsync` still carries a list, although only ONE caller comes: it
        // answers the pairing and the length check, and those are the same for
        // one human being as for twenty.
        var verdicts = await AskAsync([subject], company, cancellationToken);

        return verdicts[0];
    }

    /// <summary>A batch question, two pairs per person, in their order.</summary>
    private async Task<IReadOnlyList<bool>> AskAsync(
        IReadOnlyList<SubjectId> subjects, TenantId company, CancellationToken cancellationToken)
    {
        var forCompany = Visibilities.ForCompany(company);

        var pairs = subjects
            .SelectMany(one => new[]
            {
                new ConsentQuestionV1(one.Value, Visibilities.Public),
                new ConsentQuestionV1(one.Value, forCompany)
            })
            .ToArray();

        if (pairs.Length > ConsentLimits.HoechsteSammelgroesse)
        {
            // The caller asks more than the contract carries. That is a
            // programming error and no information about a human being — hence
            // loud, not as "not released".
            throw new ConsentGateSilent(
                $"{pairs.Length} pairs exceed "
                + $"{ConsentLimits.HoechsteSammelgroesse}");
        }

        var response = await SendAsync(pairs, cancellationToken);

        if (response.Results.Count != pairs.Length)
        {
            // An answer that does not fit the questions cannot be aligned — and
            // aligning it wrongly would mean showing the wrong person's profile.
            // So refuse instead of guessing.
            throw new ConsentGateSilent(
                $"{response.Results.Count} answers to {pairs.Length} questions");
        }

        // Two answers per person, in the order of the questions: public OR for
        // this company.
        return [.. Enumerable.Range(0, subjects.Count).Select(index =>
            Holds(response.Results[index * 2]) || Holds(response.Results[(index * 2) + 1]))];
    }

    /// <summary>
    /// A deleted capability logically withdraws the permission.
    /// </summary>
    /// <remarks>
    /// Checking both keeps the intent right even if the ledger ever reports
    /// both at once.
    /// </remarks>
    private static bool Holds(ConsentResponseV1 response) =>
        response.Granted && !response.Deleted;

    private async Task<ConsentBatchResponseV1> SendAsync(
        IReadOnlyList<ConsentQuestionV1> pairs, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(new Uri(_settings.Adresse), "/consent/check-batch"))
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
            // Only the kind, never the content: what was asked is a list of
            // human beings and does not belong in the log.
            log.LogWarning("The consent ledger is unreachable: {Kind}",
                error.GetType().Name);

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
                log.LogWarning("The consent ledger answered with {Status}.",
                    (int)response.StatusCode);

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
