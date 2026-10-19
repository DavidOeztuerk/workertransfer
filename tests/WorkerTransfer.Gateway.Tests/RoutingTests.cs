using Noelia.Abstractions.Observability;
using FluentAssertions;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>Where which address points — measured, not read.</summary>
/// <remarks>
/// The route map is the whole service. Checking it by reading it is the same as
/// writing it; that is why the real gateway runs here with the shipped file in
/// front of eleven fakes, and every line is a question to a real address.
/// </remarks>
[Collection(LandscapeCollection.Name)]
public class RoutingTests(Landscape landscape)
{
    /// <summary>The ordinary cases: one prefix, one service.</summary>
    [Theory]
    [InlineData("/auth/login", "identity")]
    [InlineData("/me", "identity")]
    [InlineData("/me/companies", "identity")]
    [InlineData("/companies", "identity")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/members", "identity")]
    [InlineData("/invitations/accept", "identity")]
    [InlineData("/account/erasure", "identity")]
    [InlineData("/consent/check", "consent")]
    [InlineData("/consent/check-batch", "consent")]
    [InlineData("/profiles/me", "profile")]
    [InlineData("/scout/candidates", "scout")]
    [InlineData("/scout/searches", "scout")]
    [InlineData("/resumes/me", "resume")]
    [InlineData("/portfolios/me", "portfolio")]
    [InlineData("/jobs", "jobs")]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000", "jobs")]
    [InlineData("/applications/me", "applications")]
    [InlineData("/market/me", "transfer")]
    [InlineData("/transfers/me", "transfer")]
    [InlineData("/github/me", "github")]
    [InlineData("/notifications", "notification")]
    [InlineData("/notifications/me", "notification")]
    [InlineData("/assessments", "assessment")]
    [InlineData("/assessments/me", "assessment")]
    [InlineData("/messages/me", "messages")]
    [InlineData("/messages/application/7f000001-0000-0000-0000-000000000000", "messages")]
    [InlineData("/messages/drafts/7f000001-0000-0000-0000-000000000000/release", "messages")]
    public async Task An_address_lands_at_its_service(string path, string expected)
    {
        var (service, arrived) = await landscape.Ask(path);

        service.Should().Be(expected);
        arrived.Should().Be(path, "the path is passed through, not rewritten");
    }

    /// <summary>The superseded path no longer hits a service.</summary>
    /// <remarks>
    /// <c>/candidates</c> was the candidate list until 11.09.2026 and fell with
    /// the move of the interface (ADR-0036). This suite is the counter-probe to
    /// the row in <c>docs/routenkarte.yml</c>: there 404 stands in all four
    /// columns, and here it stands that really no route is behind it any more —
    /// not merely one that happens to answer nothing right now.
    /// </remarks>
    [Theory]
    [InlineData("/candidates")]
    [InlineData("/candidates/7f000001-0000-0000-0000-000000000000")]
    public async Task The_superseded_path_no_longer_hits_a_service(string path)
    {
        var (service, _) = await landscape.Ask(path);

        service.Should().Be("<no route>");
    }

    /// <summary>
    /// The exceptions: addresses that differ only in the last segment.
    /// </summary>
    /// <remarks>
    /// This is the place where a gateway without priorities gets it wrong.
    /// </remarks>
    [Theory]
    [InlineData("/jobs/7f000001-0000-0000-0000-000000000000/applications", "applications")]
    [InlineData("/companies/me/jobs", "jobs")]
    [InlineData("/companies/me/application-stats", "applications")]
    [InlineData("/companies/me/profile", "companies")]
    [InlineData("/companies/by-slug/example-ltd", "companies")]
    [InlineData("/companies/7f000001-0000-0000-0000-000000000000/profile", "companies")]
    [InlineData("/me/notification-preferences", "notification")]
    public async Task The_more_precise_rule_wins(string path, string expected)
    {
        var (service, _) = await landscape.Ask(path);

        service.Should().Be(expected);
    }

    /// <summary>
    /// A service-to-service entrance never reaches the service that implements
    /// it from outside.
    /// </summary>
    /// <remarks>
    /// All three lie behind a shared secret. Reachable over the public origin
    /// they would be "delete everything about this person", guarded by a header.
    /// <para>
    /// What is checked is therefore not "no route" but what is really promised:
    /// <strong>the owner does not see the request.</strong> The difference can
    /// be seen at <c>/companies/withdrawal</c> — without a route of its own the
    /// path falls onto <c>/companies/{rest}</c> and lands at identity-service,
    /// which does not know it and answers with a 404. That is exactly right: no
    /// handler runs, and the answer confirms nothing. Calling it "no route"
    /// would be a promise the map does not give.
    /// </para>
    /// </remarks>
    [Theory]
    // Eight services implement `/erasure` — none of them may see it.
    [InlineData("/internal/notifications", "notification")]
    [InlineData("/erasure", "consent")]
    [InlineData("/erasure", "profile")]
    [InlineData("/erasure", "resume")]
    [InlineData("/erasure", "portfolio")]
    [InlineData("/erasure", "applications")]
    [InlineData("/erasure", "transfer")]
    [InlineData("/erasure", "github")]
    [InlineData("/erasure", "notification")]
    [InlineData("/internal/notify", "identity")]
    // Until D2 it stood in the map with priority 100 and answered publicly
    // with a 401 — thereby confirming that the endpoint exists. The route was
    // never needed: identity-service calls jobs-service directly.
    [InlineData("/companies/withdrawal", "jobs")]
    public async Task A_service_entrance_does_not_reach_its_owner(
        string path, string owner)
    {
        var (service, _) = await landscape.Ask(path);

        service.Should().NotBe(owner);
    }

    /// <summary>The gateway answers the health probes itself.</summary>
    /// <remarks>
    /// Otherwise a single sick service would tip the gateway out of the load
    /// balancer and take the other nine with it.
    /// </remarks>
    [Fact]
    public async Task The_health_probe_is_not_passed_on()
    {
        using var response = await landscape.Browser.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().Contain("live");
    }

    /// <summary>Every request gets an id before it branches.</summary>
    [Fact]
    public async Task Without_an_id_one_is_granted()
    {
        using var response = await landscape.Browser.GetAsync("/jobs");

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Should().NotBeEmpty();
    }

    /// <summary>An id that was brought along is passed on, not overwritten.</summary>
    /// <remarks>
    /// It then comes from a caller that already keeps a chain.
    /// </remarks>
    [Fact]
    public async Task An_id_that_was_brought_along_stays()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/jobs");
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "my-chain");

        using var response = await landscape.Browser.SendAsync(request);

        response.Headers.GetValues(CorrelationId.HeaderName).Single().Should().Be("my-chain");
    }
}
