using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Domain.Connections;
using WorkerTransfer.GitHub.Infrastructure.Network;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>A GitHub made of paper.</summary>
public sealed class CannedResponse(
    Func<HttpRequestMessage, (HttpStatusCode Code, string Body)> response)
    : HttpMessageHandler
{
    /// <summary>Which paths were asked for.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>The requests themselves — for probes about headers.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Paths.Add(request.RequestUri!.PathAndQuery);
        Requests.Add(request);

        var (code, body) = response(request);

        return Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

/// <summary>A factory that always delivers the same client.</summary>
public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// The real client against a GitHub made of paper.
/// </summary>
/// <remarks>
/// This suite exists because a counter-probe showed a gap: the journey tests
/// replace <see cref="IGitHub"/> with a probe, and so everything <em>inside</em>
/// the client went unchecked — above all the fork filter. Leaving forks out is
/// no cosmetic: a copy of somebody else's work is no evidence of one's own, and
/// showing it under "my repositories" would be exactly the silent assertion
/// ADR-0022 excludes.
/// </remarks>
public class HttpGitHubTests
{
    /// <summary>Answers with this listing — and empty to every language question.</summary>
    /// <remarks>
    /// Without the separation a <c>/languages</c> request would get the
    /// repository <em>listing</em> as its answer. That is no dictionary, so it
    /// throws — a real error, but one that would lie in the probe and not in the
    /// code. Since the languages are fetched without a token too, this strikes
    /// every probe that answers blanket.
    /// </remarks>
    private static CannedResponse ListOnly(string list) =>
        new(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "{}")
                : (HttpStatusCode.OK, list));

    private static HttpGitHub Build(CannedResponse paper, string token = "") =>
        new(new SingleClientFactory(paper),
            Options.Create(new GitHubSettings
            {
                BaseUrl = "https://api.github.test",
                Token = token
            }),
            NullLogger<HttpGitHub>.Instance);

    /// <summary>
    /// <strong>Every request carries a <c>User-Agent</c> — otherwise GitHub
    /// answers 403.</strong>
    /// </summary>
    /// <remarks>
    /// No style slip but the reason this service never worked. GitHub demands
    /// the header and refuses EVERYTHING without it — not with a 400, not with a
    /// message naming the word, but with a 403 that looks like a rate limit.
    /// Measured 04.09.2026 against the same address: with the header 200,
    /// without it 403.
    /// <para>
    /// For as long as it was missing, the gist proof failed too — nobody could
    /// ever complete a connection, and the interface said "github unavailable"
    /// about it, which looks like an outage at GitHub.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Every_request_carries_a_user_agent()
    {
        var paper = new CannedResponse(_ => (HttpStatusCode.OK, "[]"));
        var service = Build(paper);

        await service.RepositoriesAsync("anna-dev");
        await service.HasProofGistAsync("anna-dev", "workertransfer-verify-abc");

        paper.Requests.Should().NotBeEmpty("otherwise the test checks nothing");
        paper.Requests.Should().OnlyContain(
            request => request.Headers.UserAgent.Count > 0);
    }

    /// <summary>
    /// <strong>The sign-in keys compose sets are the keys the settings
    /// read.</strong>
    /// </summary>
    /// <remarks>
    /// A settings property name IS the environment key. <c>Rueckadresse</c> was
    /// once renamed to <c>RedirectUrl</c> on its own: compose went on setting
    /// <c>GitHub__OAuth__Rueckadresse</c>, the property stayed empty, and the
    /// sign-in counted as not configured — over a green build and a green
    /// suite, because every journey replaces the sign-in with a probe. This
    /// binds exactly the keys compose names, with a value each, and requires
    /// the sign-in to count as configured.
    /// </remarks>
    [Fact]
    public void The_sign_in_keys_from_compose_bind()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WorkerTransfer.slnx")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository root has to be found");

        var keys = System.Text.RegularExpressions.Regex
            .Matches(
                File.ReadAllText(Path.Combine(root!.FullName, "docker-compose.yml")),
                @"^\s*GitHub__OAuth__(?<key>\w+):",
                System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups["key"].Value)
            .ToList();

        keys.Should().HaveCountGreaterThanOrEqualTo(3, "otherwise this test checks nothing");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(keys.Select(key =>
                new KeyValuePair<string, string?>($"{GitHubSignInSettings.Section}:{key}", "set")))
            .Build();

        var settings = new GitHubSignInSettings();
        configuration.GetSection(GitHubSignInSettings.Section).Bind(settings);

        var signIn = new HttpGitHubSignIn(
            new SingleClientFactory(new CannedResponse(_ => (HttpStatusCode.OK, "{}"))),
            Options.Create(settings),
            Options.Create(new GitHubSettings()));

        signIn.IsConfigured.Should().BeTrue(
            "every key compose sets has to land in a property of the same name");
    }

    /// <summary>The topics travel along, because they are a NAMING.</summary>
    /// <remarks>
    /// The strongest evidence of all: a human being wrote "kubernetes" onto the
    /// repository, no counter derived it. And they cost nothing — GitHub has
    /// delivered them in the same listing since 2022 without anything being
    /// done.
    /// </remarks>
    [Fact]
    public async Task Topics_travel_along()
    {
        var paper = ListOnly("""
            [{"name":"control-room","description":"","language":"TypeScript",
              "stargazers_count":0,"html_url":"https://github.com/anna/control-room",
              "pushed_at":"2026-08-01T10:00:00Z","fork":false,
              "topics":["react","kubernetes","observability"]}]
            """);

        var entry = (await Build(paper).RepositoriesAsync("anna-dev")).Repositories.Single();

        entry.Topics.Should().Equal("react", "kubernetes", "observability");
    }

    /// <summary>A repository without topics is not an error, just empty.</summary>
    [Fact]
    public async Task Without_topics_the_list_stays_empty()
    {
        var paper = new CannedResponse(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, "{}")
                : (HttpStatusCode.OK, """
                    [{"name":"still","description":"","language":null,
                      "stargazers_count":0,"html_url":"https://github.com/anna/still",
                      "pushed_at":null,"fork":false}]
                    """));

        var entry = (await Build(paper).RepositoriesAsync("anna-dev")).Repositories.Single();

        entry.Topics.Should().BeEmpty();
        entry.Languages.Should().BeEmpty();
    }

    /// <summary>
    /// <strong>Without a token the languages still come — only for fewer
    /// repositories.</strong>
    /// </summary>
    /// <remarks>
    /// The opposite used to stand here, and it was a measured harm: GitHub
    /// reports seven languages for <c>workertransfer</c>, the page showed one.
    /// The reason was the rate limit — sixty requests an hour — but sixty are
    /// perfectly enough for ten repositories per fetch. Going without saved
    /// nothing and cost half the information.
    /// </remarks>
    [Fact]
    public async Task Without_a_token_the_languages_still_come()
    {
        var paper = new CannedResponse(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Python":70,"TypeScript":26,"CSS":1}""")
                : (HttpStatusCode.OK, """
                    [{"name":"tool","description":"","language":"Python",
                      "stargazers_count":0,"html_url":"https://github.com/anna/tool",
                      "pushed_at":null,"fork":false}]
                    """));

        var snapshot = await Build(paper).RepositoriesAsync("anna-dev");

        snapshot.Repositories.Single().Languages
            .Should().BeEquivalentTo(["Python", "TypeScript", "CSS"]);

        // And the shares stay out, with or without a token (ADR-0022 §2).
        System.Text.Json.JsonSerializer.Serialize(snapshot.Repositories.Single())
            .Should().NotContain("70");

        snapshot.LanguagesComplete.Should().BeTrue();
    }

    /// <summary>
    /// <strong>When the budget does not stretch, the snapshot says so — instead
    /// of looking narrow.</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0022 §3: "Whoever has nothing on GitHub is not worse, but elsewhere.
    /// A view that does not say so lies by omission." The same holds one level
    /// down — at a repository whose languages we could not fetch, the main
    /// language would read like the only one.
    /// </remarks>
    [Fact]
    public async Task More_repositories_than_budget_report_themselves_incomplete()
    {
        var many = string.Join(",", Enumerable.Range(0, 11).Select(number =>
            "{\"name\":\"repo" + number + "\",\"description\":\"\","
            + "\"language\":\"Go\",\"stargazers_count\":0,"
            + "\"html_url\":\"https://github.com/anna/repo" + number + "\","
            + "\"pushed_at\":null,\"fork\":false}"));

        var paper = new CannedResponse(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":1}""")
                : (HttpStatusCode.OK, $"[{many}]"));

        var snapshot = await Build(paper).RepositoriesAsync("anna-dev");

        snapshot.Repositories.Should().HaveCount(11);
        snapshot.LanguagesComplete.Should().BeFalse();

        // The ten most recently worked on have their languages — GitHub
        // delivers sorted by `pushed`, so the budget strikes the oldest.
        snapshot.Repositories.Take(10).Should().OnlyContain(entry => entry.Languages.Count == 1);
        snapshot.Repositories.Last().Languages.Should().BeEmpty();
    }

    /// <summary>
    /// <strong>The languages come as a SET — the bytes stay out.</strong>
    /// </summary>
    /// <remarks>
    /// GitHub answers with <c>{"Go": 120000, "Shell": 210}</c>. Out of exactly
    /// those numbers the deleted package computed its "skill" as
    /// <c>bytes / total_bytes</c> — "a checked-in dependency beats every careful
    /// library. Whoever writes little and well loses" (ADR-0022 §2). What is not
    /// stored nobody can add up.
    /// </remarks>
    [Fact]
    public async Task Languages_come_as_a_set_without_bytes()
    {
        var paper = new CannedResponse(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":120000,"Shell":210,"Dockerfile":90}""")
                : (HttpStatusCode.OK, """
                    [{"name":"tool","description":"","language":"Go",
                      "stargazers_count":0,"html_url":"https://github.com/anna/tool",
                      "pushed_at":null,"fork":false}]
                    """));

        var entry = (await Build(paper, token: "a-token")
            .RepositoriesAsync("anna-dev")).Repositories.Single();

        entry.Languages.Should().BeEquivalentTo(["Go", "Shell", "Dockerfile"]);

        // The numbers must not have travelled along anywhere — not even as text.
        var stored = System.Text.Json.JsonSerializer.Serialize(entry);
        stored.Should().NotContain("120000");
        stored.Should().NotContain("210");
    }

    /// <summary>Without a token the question is asked, but not arbitrarily often.</summary>
    /// <remarks>
    /// Until 05.09.2026 the assertion stood here that without a token nothing
    /// is asked at all — with the reasoning that half-filled evidence is worse
    /// than honestly empty evidence. Measured against a real account, the empty
    /// evidence was not honest: it showed "Python" where GitHub reports seven
    /// languages, and said nowhere that it knows only one. The question is now
    /// asked for the ten most recently worked on repositories — eleven requests
    /// out of sixty — and what lies beyond that the snapshot reports as
    /// incomplete.
    /// </remarks>
    [Fact]
    public async Task Without_a_token_the_number_of_language_queries_stays_capped()
    {
        var many = string.Join(",", Enumerable.Range(0, 25).Select(number =>
            "{\"name\":\"repo" + number + "\",\"description\":\"\","
            + "\"language\":\"Go\",\"stargazers_count\":0,"
            + "\"html_url\":\"https://github.com/anna/repo" + number + "\","
            + "\"pushed_at\":null,\"fork\":false}"));

        var paper = new CannedResponse(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/languages", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, """{"Go":1}""")
                : (HttpStatusCode.OK, $"[{many}]"));

        await Build(paper).RepositoriesAsync("anna-dev");

        paper.Paths
            .Count(path => path.Contains("/languages", StringComparison.Ordinal))
            .Should().Be(10);
    }

    /// <summary>A fork is no evidence of one's own work.</summary>
    [Fact]
    public async Task Forks_stay_out()
    {
        var paper = ListOnly("""
            [
              {"name":"own-work","description":"mine","language":"Go",
               "stargazers_count":3,"html_url":"https://github.com/anna/own-work",
               "pushed_at":"2026-08-01T10:00:00Z","fork":false},
              {"name":"copied","description":"someone-elses","language":"Rust",
               "stargazers_count":9000,"html_url":"https://github.com/anna/copied",
               "pushed_at":"2026-08-20T10:00:00Z","fork":true}
            ]
            """);

        var found = await Build(paper).RepositoriesAsync("anna-dev");

        found.Repositories.Select(entry => entry.Name).Should().Equal("own-work");
    }

    /// <summary>Every field comes from GitHub, none is computed.</summary>
    [Fact]
    public async Task The_fields_are_copied()
    {
        var paper = ListOnly("""
            [{"name":"tool","description":"A tool","language":"Go",
              "stargazers_count":42,"html_url":"https://github.com/anna/tool",
              "pushed_at":"2026-08-01T10:00:00Z","fork":false,
              "topics":["cli","distributed-systems"]}]
            """);

        var entry = (await Build(paper).RepositoriesAsync("anna-dev")).Repositories.Single();

        // `BeEquivalentTo` and not `Be`: since `Repository` carries two lists,
        // a `record`'s equality compares them by REFERENCE — two identically
        // filled lists are therefore never equal. The comparison has to be
        // structural, or it only checks whether it is the same object.
        entry.Should().BeEquivalentTo(new Repository(
            "tool", "A tool", "Go", 42,
            "https://github.com/anna/tool",
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero),
            // Without a token the languages are not asked for per repository —
            // sixty requests an hour do not stretch to that.
            [],
            ["cli", "distributed-systems"]));
    }

    /// <summary>Missing fields are not invented.</summary>
    [Fact]
    public async Task What_is_missing_stays_empty_instead_of_guessed()
    {
        var paper = ListOnly("""
            [{"name":"still","description":null,"language":null,
              "stargazers_count":0,"html_url":"https://github.com/anna/still",
              "pushed_at":null,"fork":false}]
            """);

        var entry = (await Build(paper).RepositoriesAsync("anna-dev")).Repositories.Single();

        entry.Description.Should().BeEmpty();
        entry.Language.Should().BeNull();
        entry.LastPushedAt.Should().BeNull();
    }

    /// <summary>The proof hangs on the description, not on the content.</summary>
    /// <remarks>
    /// The gist list delivers the description along with it; a content would
    /// need one fetch per gist. At sixty requests an hour without a token that
    /// is no detail.
    /// </remarks>
    [Fact]
    public async Task The_gist_is_found_over_its_description()
    {
        var paper = ListOnly("""
            [{"description":"something-else"},
             {"description":"workertransfer-verify-abc123"}]
            """);

        var github = Build(paper);

        (await github.HasProofGistAsync("anna-dev", "abc123")).Should().BeTrue();
        (await github.HasProofGistAsync("anna-dev", "xyz789")).Should().BeFalse();
    }

    /// <summary>"Does not exist" is an answer, not an outage.</summary>
    [Fact]
    public async Task An_unknown_account_is_not_an_outage()
    {
        var paper = new CannedResponse(_ => (HttpStatusCode.NotFound, "{}"));
        var github = Build(paper);

        (await github.RepositoriesAsync("does-not-exist")).Repositories.Should().BeEmpty();
        (await github.HasProofGistAsync("does-not-exist", "abc")).Should().BeFalse();
    }

    /// <summary>A rate limit is a system state, not a result.</summary>
    /// <remarks>
    /// Mapping it to "not proved" would mean denying somebody their proof
    /// because we could not ask just now.
    /// </remarks>
    [Fact]
    public async Task A_rate_limit_becomes_GitHubSilent()
    {
        var paper = new CannedResponse(_ => ((HttpStatusCode)403, "rate limited"));

        var attempt = async () => await Build(paper).RepositoriesAsync("anna-dev");

        await attempt.Should().ThrowAsync<GitHubSilent>();
    }

    /// <summary>An unusable body likewise.</summary>
    [Fact]
    public async Task An_unusable_body_becomes_GitHubSilent()
    {
        var paper = new CannedResponse(_ => (HttpStatusCode.OK, "no json"));

        var attempt = async () => await Build(paper).RepositoriesAsync("anna-dev");

        await attempt.Should().ThrowAsync<GitHubSilent>();
    }

    /// <summary>What is asked for is one's own repositories, sorted by date.</summary>
    /// <remarks>
    /// <c>type=owner</c> leaves memberships out, <c>sort=pushed</c> makes the
    /// one page an honest selection: the most recently changed.
    /// </remarks>
    [Fact]
    public async Task What_is_asked_for_is_ones_own_and_most_recently_changed()
    {
        var paper = new CannedResponse(_ => (HttpStatusCode.OK, "[]"));

        await Build(paper).RepositoriesAsync("anna-dev");

        paper.Paths.Single().Should()
            .Be($"/users/anna-dev/repos?per_page={HttpGitHub.PerPage}&sort=pushed&type=owner");
    }
}
