using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hits;

namespace WorkerTransfer.Scout.Infrastructure.GitHubEvidence;

/// <summary>Where the evidence lies.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>GitHub__Adresse</c>, <c>GitHub__Geduld</c>, <c>GitHub__HoechstzahlBelege</c>),
/// so they stay German until the keys are renamed everywhere at once — in
/// compose, in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class EvidenceSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "GitHub";

    /// <summary>The base address of github-service.</summary>
    /// <remarks>
    /// <strong>Not</strong> that of github.com. This service never asks GitHub
    /// itself — github-service does, at a person's push of a button and never in
    /// the background (ADR-0004, ADR-0033). The address stands in the
    /// configuration because the egress boundary derives its allowed hosts from
    /// there.
    /// </remarks>
    public string Adresse { get; set; } = "http://github-service:8011";

    /// <summary>How long to wait for an answer.</summary>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many pieces of evidence a hit carries at most.</summary>
    /// <remarks>
    /// A cap, so that a hit list does not become a repository list — and
    /// expressly no <em>selection</em> by quality: it is cut in the order in
    /// which github-service delivers. Whoever sorted out by stars would have
    /// built a ranking (ADR-0022).
    /// </remarks>
    public int HoechstzahlBelege { get; set; } = 24;
}

/// <summary>Fetches evidence to a hit — never to find it.</summary>
/// <remarks>
/// <para><c>GET /github/{subjectId}</c> checks the release
/// (<c>github.visibility:public</c>) and the proof itself: a merely named
/// connection is not handed out by that service, otherwise somebody would show
/// the repositories of a foreign account as their own (ADR-0033). It is asked
/// on the caller's behalf, so that the same decision falls there as
/// everywhere.</para>
///
/// <para><strong>A failure is no failure of the search.</strong> This class does
/// not throw: the hit stays, and its <see cref="EvidenceState"/> says what is
/// missing. A 404 means "nothing released" and is <em>no judgement</em>; an
/// outage means "unavailable", and that is something else again. Naming the two
/// alike would be a statement about a human being that stems from our outage
/// (ADR-0022 §3).</para>
/// </remarks>
public sealed class HttpEvidenceSource(
    IHttpClientFactory factory,
    IHttpContextAccessor accessor,
    IOptions<EvidenceSettings> settings,
    ILogger<HttpEvidenceSource> log) : IEvidenceSource
{
    /// <summary>The name the client is registered under.</summary>
    public const string Client = "github";

    private readonly EvidenceSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<EvidenceSheet> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            return new EvidenceSheet([], EvidenceState.Unavailable);
        }

        ForeignConnectionV1? connection;

        try
        {
            connection = await Fetch(subject, cancellationToken);
        }
        catch (Exception error)
            when (error is HttpRequestException or TaskCanceledException
                  or NotSupportedException or JsonException)
        {
            // The kind, never the content.
            //
            // `JsonException` stands here expressly: an unreadable answer is the
            // same case as no answer — and without this branch it would leave the
            // service as a 500 and take the WHOLE page of hits with it, although
            // only one piece of evidence is missing. A failure of the evidence is
            // no failure of the search.
            log.LogWarning(
                "github-service is unreachable: {Kind}", error.GetType().Name);

            return new EvidenceSheet([], EvidenceState.Unavailable);
        }

        if (connection is null)
        {
            // 404: not there, not proven, not released — and the three answer
            // alike there on purpose. Here it simply means: nothing is available.
            // Not: there is nothing.
            return new EvidenceSheet([], EvidenceState.NoneReleased);
        }

        var evidence = (connection.Repositories ?? [])
            .SelectMany(From)
            // Deduplicated over word AND project: the same topic on two
            // repositories is two pieces of evidence, on one repository one.
            .DistinctBy(item => (item.Word, item.Project, item.Kind))
            .Take(Math.Max(1, _settings.HoechstzahlBelege))
            .ToArray();

        if (evidence.Length == 0)
        {
            return new EvidenceSheet([], EvidenceState.NoneReleased);
        }

        return new EvidenceSheet(
            evidence,
            connection.LanguagesComplete ? EvidenceState.Complete : EvidenceState.Partial);
    }

    /// <summary>Topics first, languages after.</summary>
    /// <remarks>
    /// <para><strong>The order orders words, never people</strong> (ADR-0033). A
    /// topic is a <em>naming</em> — a human being wrote "kubernetes" onto this
    /// repository; a language name is GitHub's detection on files. What was
    /// named stands in front, and no rank about anybody follows from this
    /// order.</para>
    ///
    /// <para>No byte count, no share, no stars: they do not even arrive
    /// here.</para>
    /// </remarks>
    private static IEnumerable<Evidence> From(ForeignRepositoryV1 repository)
    {
        foreach (var topic in repository.Topics ?? [])
        {
            yield return new Evidence(topic, EvidenceKind.Topic, repository.Name, repository.Url);
        }

        foreach (var language in repository.Languages ?? [])
        {
            yield return new Evidence(language, EvidenceKind.Language, repository.Name, repository.Url);
        }
    }

    private async Task<ForeignConnectionV1?> Fetch(SubjectId subject, CancellationToken cancellationToken)
    {
        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(_settings.Adresse), $"/github/{subject.Value}"));

        // On the caller's behalf: github-service checks the release itself, and
        // the same decision is to fall as for a browser.
        if (CallerToken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            log.LogWarning(
                "github-service answered with {Status}.", (int)response.StatusCode);

            throw new HttpRequestException("github-service returned an error status");
        }

        return await response.Content.ReadFromJsonAsync<ForeignConnectionV1>(cancellationToken);
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
