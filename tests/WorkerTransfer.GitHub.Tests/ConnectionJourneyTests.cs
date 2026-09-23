using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Claim, prove, show, disconnect.</summary>
[Collection(PostgresCollection.Name)]
public class ConnectionJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGitHub _github = new();
    private readonly ProbeSignIn _signIn = new();
    private readonly ProbeLedger _ledger = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:github", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IGitHub>(_ => _github));
                services.Replace(ServiceDescriptor.Scoped<IGitHubSignIn>(_ => _signIn));
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsPerson(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.Person(subject));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The <c>state</c> out of the sign-in address.</summary>
    private static string StateOf(string address) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(address).Query)["state"]!;

    private static Task<HttpResponseMessage> Connect(HttpClient browser, string login) =>
        browser.PostAsJsonAsync("/github/me", new { login });

    private static Repository Repo(
        string name, string? language = "Go", int stars = 0, int daysOld = 0) =>
        new(name, $"A repository called {name}", language, stars,
            $"https://github.com/anna/{name}",
            DateTimeOffset.UnixEpoch.AddDays(1000 - daysOld),
            language is null ? [] : [language], ["distributed-systems"]);

    /// <summary>Whoever NAMED an account has to prove that same one.</summary>
    /// <remarks>
    /// The comparison still holds — only where there is something to compare.
    /// Whoever named nothing gets the reported name written in
    /// (see <see cref="Signing_in_without_a_naming_writes_in_the_reported_name" />).
    /// </remarks>
    [Fact]
    public async Task Name_sign_in_see()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        var opened = await Json(await Connect(asAnna, "anna-dev"));
        var state = opened.GetProperty("challenge_description").GetString()!
            .Replace("workertransfer-verify-", string.Empty, StringComparison.Ordinal);

        var start = await Json(await asAnna.PostAsync("/github/me/oauth/start", null));
        start.GetProperty("url").GetString().Should().Contain(state);

        // What the browser would do at GitHub is, here, a code that leads to
        // the login name.
        _signIn.Codes["a-code"] = "anna-dev";
        _github.Repos["anna-dev"] = [Repo("tool")];

        var done = await Json(await asAnna.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "a-code", state = state }));

        done.GetProperty("verified").GetBoolean().Should().BeTrue();
        done.GetProperty("challenge_description").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// <strong>Whoever signs in as somebody else proves nothing.</strong>
    /// </summary>
    /// <remarks>
    /// The most dangerous case of the whole flow: somebody enters
    /// <c>torvalds</c> and signs in with their own account. Without the name
    /// comparison a proof for a foreign account would stand afterwards — and
    /// that account's work would appear as theirs.
    /// </remarks>
    [Fact]
    public async Task Signing_in_with_a_foreign_account_proves_nothing()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        var opened = await Json(await Connect(asAnna, "torvalds"));
        var state = opened.GetProperty("challenge_description").GetString()!
            .Replace("workertransfer-verify-", string.Empty, StringComparison.Ordinal);

        _signIn.Codes["my-code"] = "anna-dev";

        var response = await asAnna.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "my-code", state = state });

        // 422 and not 404: the request was fine, the proof was missing.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var afterwards = await Json(await asAnna.GetAsync("/github/me"));
        afterwards.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>A foreign state proves nothing.</summary>
    /// <remarks>
    /// It is the protection against a planted return: without it somebody
    /// could slip a signed-in person their own code and thereby hang their
    /// account on them.
    /// </remarks>
    [Fact]
    public async Task A_foreign_state_proves_nothing()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        await Connect(asAnna, "anna-dev");
        _signIn.Codes["a-code"] = "anna-dev";

        var response = await asAnna.PostAsJsonAsync(
            "/github/me/oauth/finish",
            new { code = "a-code", state = "a-different-state" });

        // 422 and not 404: the request was fine, the proof was missing.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var afterwards = await Json(await asAnna.GetAsync("/github/me"));
        afterwards.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>Without credentials there is no address — and the gist stays.</summary>
    [Fact]
    public async Task Without_a_configured_sign_in_there_is_no_address()
    {
        _signIn.IsConfigured = false;

        var asAnna = AsPerson(Guid.CreateVersion7());
        await Connect(asAnna, "anna-dev");

        var start = await Json(await asAnna.PostAsync("/github/me/oauth/start", null));

        start.GetProperty("url").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Without a named account the sign-in still leads there.</summary>
    /// <remarks>
    /// The opposite expectation used to stand here, and it was the GIST's
    /// requirement in a place where the gist has no business: it has to know
    /// whose gists it searches — the sign-in does not, because GitHub reports
    /// only the account that consented. Measured 05.09.2026 against a real
    /// account: the naming step protected nobody but locked out the person
    /// whose connection still carried an old name.
    /// </remarks>
    [Fact]
    public async Task Without_a_named_account_the_sign_in_still_leads_there()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        var start = await Json(await asAnna.PostAsync("/github/me/oauth/start", null));

        start.GetProperty("url").GetString().Should().NotBeNullOrEmpty();

        // And the connection that comes into being ASSERTS NOTHING: no account,
        // no proof.
        var afterwards = await Json(await asAnna.GetAsync("/github/me"));
        afterwards.GetProperty("login").ValueKind.Should().Be(JsonValueKind.Null);
        afterwards.GetProperty("verified").GetBoolean().Should().BeFalse();
    }

    /// <summary>Whoever named nothing gets the reported name.</summary>
    /// <remarks>
    /// The replacement for the name comparison, and it is not weaker: GitHub
    /// reports exclusively the account that gave the consent. A foreign one
    /// cannot be slipped in here, because it cannot be named in the first
    /// place.
    /// </remarks>
    [Fact]
    public async Task Signing_in_without_a_naming_writes_in_the_reported_name()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        var start = await Json(await asAnna.PostAsync("/github/me/oauth/start", null));
        var state = StateOf(start.GetProperty("url").GetString()!);

        _signIn.Codes["a-code"] = "anna-dev";
        _github.Repos["anna-dev"] = [Repo("tool")];

        var done = await Json(await asAnna.PostAsJsonAsync(
            "/github/me/oauth/finish", new { code = "a-code", state = state }));

        done.GetProperty("login").GetString().Should().Be("anna-dev");
        done.GetProperty("verified").GetBoolean().Should().BeTrue();
    }

    /// <summary>The answer whether this server offers the sign-in.</summary>
    /// <remarks>
    /// It creates nothing — unlike <c>/me/oauth/start</c>. Exactly that is why
    /// the interface may ask it when the page is viewed, without leaving a row
    /// behind for every visit.
    /// </remarks>
    [Fact]
    public async Task The_configuration_can_be_asked_for_and_creates_nothing()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        var asked = await Json(await asAnna.GetAsync("/github/oauth"));
        asked.GetProperty("available").GetBoolean().Should().BeTrue();

        // Without a connection `/github/me` answers 200 and JSON `null` — the
        // question is allowed, the answer is "none".
        (await Json(await asAnna.GetAsync("/github/me"))).ValueKind
            .Should().Be(JsonValueKind.Null);

        _signIn.IsConfigured = false;

        var unconfigured = await Json(await asAnna.GetAsync("/github/oauth"));
        unconfigured.GetProperty("available").GetBoolean().Should().BeFalse();
    }

    /// <summary>The whole path: name, create the gist, prove, see.</summary>
    [Fact]
    public async Task Name_prove_see()
    {
        var anna = Guid.CreateVersion7();
        var asAnna = AsPerson(anna);

        var named = await Connect(asAnna, "anna-dev");

        named.StatusCode.Should().Be(HttpStatusCode.OK);

        var opened = await Json(named);
        opened.GetProperty("verified").GetBoolean().Should().BeFalse();

        var description = opened.GetProperty("challenge_description").GetString();
        description.Should().StartWith("workertransfer-verify-");

        // The person creates the gist — that is the whole proof.
        _github.Gists["anna-dev"] = [description!];
        _github.Repos["anna-dev"] = [Repo("tool"), Repo("library")];

        var proved = await asAnna.PostAsync("/github/me/verify", null);

        proved.StatusCode.Should().Be(HttpStatusCode.OK);

        var done = await Json(proved);
        done.GetProperty("verified").GetBoolean().Should().BeTrue();
        done.GetProperty("repositories").GetArrayLength().Should().Be(2);

        // ConnectAndProve means: the one-time string is of use to nobody any more.
        done.GetProperty("challenge_description").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// On naming, GitHub is NOT asked.
    /// </summary>
    /// <remarks>
    /// For as long as nothing is proved there is nothing to fetch — and a fetch
    /// would only tell GitHub that somebody asked about this account.
    /// </remarks>
    [Fact]
    public async Task On_naming_GitHub_is_not_asked()
    {
        await Connect(AsPerson(Guid.CreateVersion7()), "someone-elses-account");

        _github.Asked.Should().BeEmpty();
    }

    /// <summary>No gist, no proof — and that is 422, not 404.</summary>
    /// <remarks>
    /// The request was fine, the proof was missing.
    /// </remarks>
    [Fact]
    public async Task Without_a_gist_it_is_422()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        await Connect(asAnna, "anna-dev");

        var attempt = await asAnna.PostAsync("/github/me/verify", null);

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Naming a different account resets the proof.
    /// </summary>
    /// <remarks>
    /// Without this reset somebody could prove one account and then change the
    /// name to a foreign one: the proof would still stand, but it would have
    /// been furnished for a different account.
    /// </remarks>
    [Fact]
    public async Task A_different_account_resets_the_proof()
    {
        var anna = Guid.CreateVersion7();
        var asAnna = AsPerson(anna);
        await ConnectAndProve(asAnna, "anna-dev");

        var renamed = await Json(await Connect(asAnna, "someone-else"));

        renamed.GetProperty("verified").GetBoolean().Should().BeFalse();
        renamed.GetProperty("repositories").GetArrayLength().Should().Be(0);
        renamed.GetProperty("challenge_description").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>
    /// A different spelling is not a different account — the proof still
    /// holds.
    /// </summary>
    [Fact]
    public async Task A_different_spelling_is_not_a_different_account()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        await ConnectAndProve(asAnna, "anna-dev");

        var same = await Json(await Connect(asAnna, "Anna-Dev"));

        same.GetProperty("verified").GetBoolean().Should().BeTrue();
        same.GetProperty("login").GetString().Should().Be("Anna-Dev");
    }

    /// <summary>
    /// The new one-time string is a new one — the old one must not go on
    /// holding.
    /// </summary>
    /// <remarks>
    /// Otherwise a gist created for the old account would prove the new one
    /// too.
    /// </remarks>
    [Fact]
    public async Task On_an_account_change_the_old_string_no_longer_holds()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        var oldString = (await Json(await Connect(asAnna, "anna-dev")))
            .GetProperty("challenge_description").GetString();

        var newString = (await Json(await Connect(asAnna, "someone-else")))
            .GetProperty("challenge_description").GetString();

        newString.Should().NotBe(oldString);

        // A gist with the OLD string does not prove the new account.
        _github.Gists["someone-else"] = [oldString!];

        (await asAnna.PostAsync("/github/me/verify", null))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Newest first — not by stars.</summary>
    /// <remarks>
    /// Stars measure visibility, not work, and a sorting is already a judgement
    /// (ADR-0022).
    /// </remarks>
    [Fact]
    public async Task Sorted_by_date_not_by_stars()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        _github.Repos["anna-dev"] =
        [
            Repo("old-and-popular", stars: 9000, daysOld: 500),
            Repo("new-and-quiet", stars: 0, daysOld: 1)
        ];

        await ConnectAndProve(asAnna, "anna-dev");

        var names = (await Json(await asAnna.GetAsync("/github/me")))
            .GetProperty("repositories").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();

        names.Should().Equal("new-and-quiet", "old-and-popular");
    }

    /// <summary>
    /// A repository without a date stands at the back, not at the front.
    /// </summary>
    [Fact]
    public async Task A_repository_without_a_date_stands_last()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());

        _github.Repos["anna-dev"] =
        [
            new Repository("without-date", string.Empty, null, 0, "https://x", null, [], []),
            Repo("with-date", daysOld: 900)
        ];

        await ConnectAndProve(asAnna, "anna-dev");

        var names = (await Json(await asAnna.GetAsync("/github/me")))
            .GetProperty("repositories").EnumerateArray()
            .Select(entry => entry.GetProperty("name").GetString())
            .ToList();

        names.Should().Equal("with-date", "without-date");
    }

    /// <summary>One's own view shows the unproved connection too.</summary>
    /// <remarks>
    /// Otherwise the person would see nothing at all after the first step and
    /// would not know which string to write into the gist.
    /// </remarks>
    [Fact]
    public async Task Ones_own_view_shows_the_unproved_one_too()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        await Connect(asAnna, "anna-dev");

        var mine = await Json(await asAnna.GetAsync("/github/me"));

        mine.GetProperty("verified").GetBoolean().Should().BeFalse();
        mine.GetProperty("challenge_description").ValueKind
            .Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>Whoever named nothing gets `null`.</summary>
    [Fact]
    public async Task Without_a_connection_ones_own_view_is_null()
    {
        var response = await AsPerson(Guid.CreateVersion7()).GetAsync("/github/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>
    /// An unproved connection does not exist from outside — with a release
    /// too.
    /// </summary>
    /// <remarks>
    /// It is an assertion, and this service does not show assertions.
    /// </remarks>
    [Fact]
    public async Task An_unproved_connection_does_not_exist_from_outside()
    {
        var anna = Guid.CreateVersion7();
        await Connect(AsPerson(anna), "anna-dev");
        _ledger.Released.Add(anna);

        var fromStranger = await AsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        fromStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Without a release a stranger sees nothing — and the same as for nobody.</summary>
    [Fact]
    public async Task Without_a_release_a_stranger_sees_nothing()
    {
        var anna = Guid.CreateVersion7();
        await ConnectAndProve(AsPerson(anna), "anna-dev");

        var stranger = AsPerson(Guid.CreateVersion7());
        var hidden = await stranger.GetAsync($"/github/{anna}");
        var nobody = await stranger.GetAsync($"/github/{Guid.CreateVersion7()}");

        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nobody.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(hidden)).GetProperty("detail").GetString()
            .Should().Be((await Json(nobody)).GetProperty("detail").GetString());
    }

    /// <summary>With a release a stranger sees the evidence — but never the string.</summary>
    [Fact]
    public async Task With_a_release_a_stranger_sees_the_evidence()
    {
        var anna = Guid.CreateVersion7();
        _github.Repos["anna-dev"] = [Repo("tool")];
        await ConnectAndProve(AsPerson(anna), "anna-dev");
        _ledger.Released.Add(anna);

        var seen = await AsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        seen.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(seen);
        body.GetProperty("repositories").GetArrayLength().Should().Be(1);
        body.GetProperty("challenge_description").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Disconnecting means erasing — the snapshot disappears with it.</summary>
    [Fact]
    public async Task Disconnecting_means_erasing()
    {
        var anna = Guid.CreateVersion7();
        var asAnna = AsPerson(anna);
        _github.Repos["anna-dev"] = [Repo("tool")];
        await ConnectAndProve(asAnna, "anna-dev");

        var disconnected = await asAnna.DeleteAsync("/github/me");

        disconnected.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var context = postgres.Context();
        (await context.Connections.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>
    /// If GitHub is silent, nobody is denied their proof.
    /// </summary>
    [Fact]
    public async Task A_silent_GitHub_becomes_503_and_not_422()
    {
        var asAnna = AsPerson(Guid.CreateVersion7());
        await Connect(asAnna, "anna-dev");
        _github.IsSilent = true;

        var attempt = await asAnna.PostAsync("/github/me/verify", null);

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>If the ledger is silent, nothing is shown and nothing denied.</summary>
    [Fact]
    public async Task A_silent_ledger_becomes_503_and_not_404()
    {
        var anna = Guid.CreateVersion7();
        await ConnectAndProve(AsPerson(anna), "anna-dev");
        _ledger.IsSilent = true;

        var attempt = await AsPerson(Guid.CreateVersion7()).GetAsync($"/github/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>A user name GitHub would never hand out is refused.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-anna")]
    [InlineData("anna-")]
    [InlineData("an--na")]
    [InlineData("anna dev")]
    [InlineData("../../etc/passwd")]
    [InlineData("anna@dev")]
    public async Task An_impossible_user_name_is_422(string login)
    {
        var response = await Connect(AsPerson(Guid.CreateVersion7()), login);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _github.Asked.Should().BeEmpty("a fetch that finds nothing anyway is dropped");
    }

    /// <summary>The leading @ is habit, not a mistake.</summary>
    [Fact]
    public async Task A_leading_at_sign_is_stripped()
    {
        var named = await Connect(AsPerson(Guid.CreateVersion7()), "@anna-dev");

        named.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(named)).GetProperty("login").GetString().Should().Be("anna-dev");
    }

    /// <summary>Without signing in nothing works.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        (await _service.CreateClient().GetAsync("/github/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _service.CreateClient().GetAsync($"/github/{Guid.CreateVersion7()}"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Names an account and proves it.</summary>
    private async Task ConnectAndProve(HttpClient browser, string login)
    {
        var description = (await Json(await Connect(browser, login)))
            .GetProperty("challenge_description").GetString();

        _github.Gists[login] = [description!];

        (await browser.PostAsync("/github/me/verify", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
