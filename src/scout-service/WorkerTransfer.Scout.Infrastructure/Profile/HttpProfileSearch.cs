using System.Net;
using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Contracts;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Infrastructure.Profile;

/// <summary>Where profile-service can be reached, and what identifies us there.</summary>
/// <remarks>
/// The section and the property names ARE the environment keys
/// (<c>Profile__Adresse</c>, <c>Profile__Geheimnis</c>, <c>Profile__Geduld</c>),
/// so they stay German until the keys are renamed everywhere at once — in
/// compose, in the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class ProfileSettings
{
    /// <summary>The section this is bound to.</summary>
    public const string Section = "Profile";

    /// <summary>The base address of profile-service.</summary>
    /// <remarks>
    /// Stands in the configuration because the egress boundary derives its
    /// allowed hosts from there (<c>ServiceFoundation.GerufeneHosts</c>). A
    /// destination that stands only in the source is refused — without a log
    /// line.
    /// </remarks>
    public string Adresse { get; set; } = "http://profile-service:8003";

    /// <summary>The shared secret of the internal doors.</summary>
    /// <remarks>
    /// <strong>Empty means: no search happens</strong> — as a failure, not as an
    /// empty result. An empty hit list would look like "there is nobody", and
    /// that is a statement about people that would stem from a missing
    /// environment variable.
    /// </remarks>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>How long to wait for an answer.</summary>
    /// <remarks>
    /// Five seconds, like every other service-to-service call here. WITHOUT this
    /// line <c>HttpClient</c>'s default applies: a HUNDRED seconds.
    /// </remarks>
    public TimeSpan Geduld { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks profile-service through the internal door.</summary>
/// <remarks>
/// <para>The profiles stay where they belong (ADR-0004): this service holds no
/// copy. A copy would go stale against every change and be a second truth at the
/// first revocation.</para>
///
/// <para>The answer is read with <see cref="ForeignProfilePageV1"/> and not with
/// the sender's type: the two services share no contract, and a shared type
/// would be a translation layer that would have to be touched on both sides at
/// once at a change. That the fields really arrive is pinned by a suite in
/// <c>WorkerTransfer.Ganzes.Tests</c> — it serialises the sender's type and
/// reads it with the receiver's.</para>
/// </remarks>
public sealed class HttpProfileSearch(
    IHttpClientFactory factory,
    IOptions<ProfileSettings> settings,
    ILogger<HttpProfileSearch> log) : IProfileSearch
{
    /// <summary>The name the client is registered under.</summary>
    public const string Client = "profile";

    private const string SecretHeader = "X-Notify-Secret";

    private readonly ProfileSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task<ProfileHitPage> SearchAsync(
        SearchFilter filter,
        int pageLength,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = new List<string>
        {
            $"limit={pageLength.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        };

        // Only NAMED words go out. There is no way at all here to send evidence
        // along — `SearchFilter` carries none (ADR-0036 condition 3).
        query.AddRange(filter.NamedWords.Select(
            word => $"skill={Uri.EscapeDataString(word)}"));

        if (filter.Location.Length > 0)
        {
            query.Add($"location={Uri.EscapeDataString(filter.Location)}");
        }

        if (filter.RemoteOnly)
        {
            query.Add("remote=true");
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        }

        var page = await Fetch<ForeignProfilePageV1>(
            $"/internal/profiles/search?{string.Join('&', query)}", cancellationToken);

        // A 404 IS NO EMPTY RESULT HERE, and that was a real defect.
        //
        // The search door has no "not found" case: it answers with a page, or it
        // does not let us in — without the shared secret it answers 404 instead
        // of 401, so as not to give itself away. Exactly that happened when
        // `Notify__Geheimnis` was missing at profile-service: the door said 404,
        // this place turned it into `items: []`, the endpoint answered 200 — and
        // the UI claimed there was nobody.
        //
        // Measured on the running stack: twelve E2E journeys red, and the route
        // map stayed green because it checks status codes and not bodies. "Not
        // set up" must not look like "there is nobody" — the same rule that
        // already stood further down for the empty secret, only at the place
        // where it was missing.
        return page is null
            ? throw new ProfileSearchSilent(
                "profile-service answered 404 to the search — the shared secret "
                + "is refused or the door is closed")
            : new ProfileHitPage([.. page.Items.Select(ToHit)], page.Next);
    }

    /// <inheritdoc />
    public async Task<ProfileHit?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var profile = await Fetch<ForeignProfileV1>(
            $"/internal/profiles/{subject.Value}", cancellationToken);

        return profile is null ? null : ToHit(profile);
    }

    private static ProfileHit ToHit(ForeignProfileV1 profile) =>
        new(
            new SubjectId(profile.SubjectId),
            profile.Headline,
            profile.Bio ?? string.Empty,
            profile.Location,
            profile.RemoteOk,
            profile.Skills ?? [],
            AvailabilityTerms.ReadCommute(profile.CommuteKm));

    /// <summary>One call. <c>null</c> means 404, everything else throws.</summary>
    /// <remarks>
    /// What a 404 MEANS is decided by the caller: for a single profile "does not
    /// exist", for the search "we do not get in". This method passes the fact on
    /// and does not interpret it.
    /// </remarks>
    private async Task<T?> Fetch<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        if (string.IsNullOrEmpty(_settings.Adresse))
        {
            throw new ProfileSearchSilent("Profile:Adresse is not set.");
        }

        if (string.IsNullOrEmpty(_settings.Geheimnis))
        {
            // As a failure and not as an empty result: "not set up" must not
            // look like "there is nobody".
            throw new ProfileSearchSilent("Profile:Geheimnis is not set.");
        }

        using var client = factory.CreateClient(Client);
        client.Timeout = _settings.Geduld;

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(new Uri(_settings.Adresse), path));

        request.Headers.Add(SecretHeader, _settings.Geheimnis);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            // The kind, never the content: what was asked is a search for human
            // beings and does not belong in the log.
            log.LogWarning(
                "profile-service is unreachable: {Kind}", error.GetType().Name);

            throw new ProfileSearchSilent("profile-service unreachable");
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            log.LogWarning("profile-service did not answer in time.");

            throw new ProfileSearchSilent($"profile-service timed out ({error.GetType().Name})");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "profile-service answered with {Status}.", (int)response.StatusCode);

                throw new ProfileSearchSilent(
                    $"profile-service returned {(int)response.StatusCode}");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                       ?? throw new ProfileSearchSilent("profile-service sent an empty answer");
            }
            catch (Exception error) when (error is not ProfileSearchSilent)
            {
                log.LogWarning("profile-service answered unintelligibly.");

                throw new ProfileSearchSilent("profile-service sent an unusable answer");
            }
        }
    }
}
