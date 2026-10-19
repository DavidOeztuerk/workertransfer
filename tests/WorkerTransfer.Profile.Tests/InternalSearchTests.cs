using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Tests;

/// <summary>The door through which scout-service searches.</summary>
/// <remarks>
/// <para>It lies under <c>/internal/</c>, has <strong>no gateway
/// route</strong> and answers 404 instead of 401 without the shared secret — a
/// 401 would confirm that the endpoint exists.</para>
///
/// <para><strong>It does not ask the ledger</strong>, and that is the most
/// delicate sentence about it. The check stands at scout-service, for the whole
/// page at once (ADR-0030, ADR-0036 decision 1): exactly ONE place decides
/// about visibility, two would be two truths. This suite pins both — that the
/// door is locked, and that it searches with OR instead of AND.</para>
///
/// <para>The skill words are rare crafts on purpose: the suites of this
/// collection share ONE database, and a count over "Go" would depend on who
/// ran before.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class InternalSearchTests(Postgres postgres) : IAsyncLifetime
{
    private const string NotifySecret = "notify-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGate _gate = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("Notify:Secret", NotifySecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Without the secret the door does not exist.</summary>
    /// <remarks>
    /// 404 and not 401: a 401 would confirm that the endpoint exists. And a wrong
    /// secret answers exactly like none at all.
    /// </remarks>
    [Fact]
    public async Task Without_the_secret_the_door_does_not_exist()
    {
        var without = await _service.CreateClient().GetAsync(
            new Uri("/internal/profiles/search", UriKind.Relative));

        without.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var wrong = _service.CreateClient();
        wrong.DefaultRequestHeaders.Add("X-Notify-Secret", "off-target");

        (await wrong.GetAsync(new Uri("/internal/profiles/search", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await wrong.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The search is an OR: whoever names ONE of the words is in.
    /// </summary>
    /// <remarks>
    /// <para><c>GET /candidates</c> searched with AND and fell on 11.09.2026;
    /// the AND branch in the store with it. The OR is no loosening but the
    /// precondition for the scout's tick list: under AND every hit fulfilled
    /// every condition, every tick would be set, and "which skill is missing"
    /// would have no answer (ADR-0036 decision 2).</para>
    ///
    /// <para><strong>The counter-probe sits in the promise itself:</strong>
    /// whoever names only ONE of the two words must be in. Were it an AND, only
    /// the one naming both would come back — and this line falls.</para>
    /// </remarks>
    [Fact]
    public async Task The_internal_search_searches_with_OR()
    {
        var one = Guid.CreateVersion7();
        var both = Guid.CreateVersion7();

        await Write(one, ["Bookbinding"]);
        await Write(both, ["Bookbinding", "Thatching"]);

        var found = await Internal(
            "/internal/profiles/search?skill=Bookbinding&skill=Thatching");

        found.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().BeEquivalentTo(
                [one, both],
                "whoever names only one of the two words belongs — otherwise it "
                + "would be an AND and every tick of the scout set");
    }

    /// <summary>It delivers named skills — and nothing about visibility.</summary>
    /// <remarks>
    /// No visibility field in the body (ADR-0020): that would be a second truth
    /// beside the ledger, and the two would disagree at the first revocation.
    /// The caller asks it itself.
    /// </remarks>
    [Fact]
    public async Task A_hit_carries_no_visibility_field()
    {
        var subject = Guid.CreateVersion7();
        await Write(subject, ["Ropemaking"]);

        var hit = await Internal($"/internal/profiles/{subject}");

        hit.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo(
            "subject_id", "headline", "bio", "location", "remote_ok", "skills",
            // ADR-0041: the STAGE travels, not a kilometre figure. Both are
            // nullable and then mean "said nothing".
            "commute_km", "relocation");

        hit.GetProperty("skills").EnumerateArray()
            .Select(entry => entry.GetString()).Should().Equal("Ropemaking");
    }

    /// <summary>
    /// It ALSO hands out profiles nobody released — on purpose.
    /// </summary>
    /// <remarks>
    /// <para>The most uncomfortable promise of this door, and that is why it
    /// stands as a test of its own. scout-service checks the release, for the
    /// whole page at once. Whoever uses this door for something else fetches the
    /// check along with it — otherwise they show profiles nobody released.</para>
    ///
    /// <para>It is behind the shared secret and without a gateway route for
    /// exactly that reason: it cannot be reached from outside.</para>
    /// </remarks>
    [Fact]
    public async Task It_does_not_ask_the_ledger_and_that_is_the_promise()
    {
        var subject = Guid.CreateVersion7();
        await Write(subject, ["Rustproofing"]);

        var before = _gate.Questions;

        var found = await Internal("/internal/profiles/search?skill=Rustproofing");

        found.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().Contain(subject, "nobody released anything here");

        _gate.Questions.Should().Be(before, "this door does not ask the ledger");
    }

    /// <summary>A profile that does not exist is a 404.</summary>
    [Fact]
    public async Task An_unknown_profile_is_a_404()
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);

        (await browser.GetAsync(new Uri($"/internal/profiles/{Guid.NewGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------------------------------------------------------------

    private async Task Write(Guid subject, string[] skills)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", TokenShape.Person(subject));

        var response = await browser.PutAsJsonAsync(
            new Uri("/profiles/me", UriKind.Relative),
            new
            {
                headline = "Developer",
                bio = "",
                location = "Berlin",
                remote_ok = true,
                skills = skills
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement> Internal(string path)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Notify-Secret", NotifySecret);

        var response = await browser.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await Json(response);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
