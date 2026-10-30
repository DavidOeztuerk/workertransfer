using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.ServiceDefaults.Ai;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Jobs.Domain.Jobs;
using WorkerTransfer.Jobs.Domain.Sources;
using WorkerTransfer.Jobs.Infrastructure.Sources;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>
/// A company's adverts from its ATS or own feed (ADR-0052): set up, proven by
/// the code, fetched, mirrored — and nothing from a board nobody proved.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SourceJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly RoleProbe _roles = new();
    private readonly BoardProbe _board = new();
    private readonly DomainProbe _domains = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "withdrawal-secret");
            host.UseSetting("environment", "Development");
            // An address in the configuration makes the egress boundary
            // enforce, as it does in the stack.
            host.UseSetting("Sources:Greenhouse", "https://boards-api.greenhouse.io");
            host.UseSetting("Sources:Personio", "jobs.personio.de");
            host.UseSetting("Sources:Every", "00:00:00");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => new ProbeDrafter()));
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _roles));
                services.Replace(ServiceDescriptor.Scoped<ISourceReader>(_ => _board));
                services.Replace(ServiceDescriptor.Scoped<ICompanyDomains>(_ => _domains));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company) => With(TokenShape.Company(Guid.CreateVersion7(), company));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<JsonElement> SetUp(HttpClient browser, string kind, string key, string mode = "draft")
    {
        var created = await browser.PostAsJsonAsync("/jobs/sources", new { kind, key, mode });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await Json(created);
    }

    private static async Task<JsonElement> Fetch(HttpClient browser, Guid source)
    {
        var fetched = await browser.PostAsync($"/jobs/sources/{source}/fetch", null);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK, await fetched.Content.ReadAsStringAsync());
        return await Json(fetched);
    }

    private static async Task<List<JsonElement>> Mine(HttpClient browser) =>
        [.. (await Json(await browser.GetAsync("/companies/me/jobs"))).EnumerateArray()];

    private static ExternalAdvert Advert(string id, string title, string description = "We weld railings.") =>
        new(id, title, description, "Berlin", RemoteMode.None, EmploymentKind.FullTime, $"https://boards.greenhouse.io/acme/jobs/{id}");

    /// <summary>The binding: nothing comes in until the board carries this source's code.</summary>
    [Fact]
    public async Task A_board_brings_nothing_until_it_carries_the_code()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var source = await SetUp(browser, "greenhouse", $"acme{Guid.NewGuid():N}"[..20]);
        var id = source.GetProperty("id").GetGuid();
        var code = source.GetProperty("code").GetString()!;

        source.GetProperty("proven_at").ValueKind.Should().Be(JsonValueKind.Null);
        code.Should().StartWith("wt-");

        _board.Next = new SourceRead.Adverts([Advert("1", "Welder"), Advert("2", "Fitter")]);
        var first = await Fetch(browser, id);

        first.GetProperty("added").GetInt32().Should().Be(0, "a typed board name proves nothing");
        first.GetProperty("source").GetProperty("outcome").GetString().Should().Be("code_missing");
        (await Mine(browser)).Should().BeEmpty();

        _board.Next = new SourceRead.Adverts([Advert("1", "Welder"), Advert("2", "Fitter", $"Verification {code}")]);
        var second = await Fetch(browser, id);

        second.GetProperty("added").GetInt32().Should().Be(2);
        second.GetProperty("source").GetProperty("outcome").GetString().Should().Be("ok");
        second.GetProperty("source").GetProperty("proven_at").ValueKind.Should().Be(JsonValueKind.String);
        second.GetProperty("source").TryGetProperty("code", out _).Should().BeFalse("a proven board needs no code any more");

        var mine = await Mine(browser);
        mine.Should().HaveCount(2);
        mine.Should().OnlyContain(job => job.GetProperty("status").GetString() == "draft", "the source's mode is draft");
        mine.Should().OnlyContain(job => job.GetProperty("mirrored").GetBoolean());
        mine.Select(job => job.GetProperty("source_url").GetString())
            .Should().Contain("https://boards.greenhouse.io/acme/jobs/1");
    }

    /// <summary>The attack the code exists for: another company names the same board.</summary>
    [Fact]
    public async Task Another_company_naming_the_same_board_gets_nothing_from_it()
    {
        var board = $"shared{Guid.NewGuid():N}"[..20];
        var owner = AsCompany(Guid.CreateVersion7());
        var stranger = AsCompany(Guid.CreateVersion7());

        var ours = await SetUp(owner, "lever", board);
        var theirs = await SetUp(stranger, "lever", board);

        // The board carries the OWNER's code — the stranger cannot write into it.
        _board.Next = new SourceRead.Adverts([Advert("7", $"Welder {ours.GetProperty("code").GetString()}")]);

        (await Fetch(stranger, theirs.GetProperty("id").GetGuid()))
            .GetProperty("added").GetInt32().Should().Be(0);
        (await Mine(stranger)).Should().BeEmpty();

        // Counter-probe: the same answer proves the owner's source.
        (await Fetch(owner, ours.GetProperty("id").GetGuid()))
            .GetProperty("added").GetInt32().Should().Be(1);
    }

    /// <summary>Mirroring: changed, gone, closed — and closed stays closed.</summary>
    [Fact]
    public async Task Adverts_follow_their_source()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var source = await SetUp(browser, "personio", $"acme{Guid.NewGuid():N}"[..20], mode: "direct");
        var id = source.GetProperty("id").GetGuid();
        var code = source.GetProperty("code").GetString()!;

        _board.Next = new SourceRead.Adverts([Advert("a", $"Welder {code}"), Advert("b", "Fitter")]);
        await Fetch(browser, id);

        var mine = await Mine(browser);
        mine.Should().OnlyContain(job => job.GetProperty("status").GetString() == "published", "the source's mode is direct");

        // Changed at the source.
        _board.Next = new SourceRead.Adverts([Advert("a", "Welder (m/w/d)"), Advert("b", "Fitter")]);
        (await Fetch(browser, id)).GetProperty("changed").GetInt32().Should().Be(1);
        (await Mine(browser)).Select(job => job.GetProperty("title").GetString()).Should().Contain("Welder (m/w/d)");

        // Gone at the source: a published advert closes, applications to it stay.
        _board.Next = new SourceRead.Adverts([Advert("a", "Welder (m/w/d)")]);
        (await Fetch(browser, id)).GetProperty("closed").GetInt32().Should().Be(1);
        (await Mine(browser)).Single(job => job.GetProperty("title").GetString() == "Fitter")
            .GetProperty("status").GetString().Should().Be("closed");

        // Back at the source: closed stays closed, and nothing is doubled.
        _board.Next = new SourceRead.Adverts([Advert("a", "Welder (m/w/d)"), Advert("b", "Fitter again")]);
        var again = await Fetch(browser, id);
        again.GetProperty("added").GetInt32().Should().Be(0);
        (await Mine(browser)).Should().HaveCount(2);
        (await Mine(browser)).Select(job => job.GetProperty("title").GetString()).Should().NotContain("Fitter again");
    }

    /// <summary>A board that does not answer today has not taken its adverts down.</summary>
    [Fact]
    public async Task A_failed_read_changes_no_advert()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var source = await SetUp(browser, "greenhouse", $"acme{Guid.NewGuid():N}"[..20], mode: "direct");
        var id = source.GetProperty("id").GetGuid();

        _board.Next = new SourceRead.Adverts([Advert("1", $"Welder {source.GetProperty("code").GetString()}")]);
        await Fetch(browser, id);

        _board.Next = new SourceRead.Failed("unreachable");
        var failed = await Fetch(browser, id);

        failed.GetProperty("source").GetProperty("outcome").GetString().Should().Be("unreachable");
        failed.GetProperty("closed").GetInt32().Should().Be(0);
        (await Mine(browser)).Single().GetProperty("status").GetString().Should().Be("published");
    }

    /// <summary>The text belongs to the source; skills and salary the company adds here.</summary>
    [Fact]
    public async Task A_mirrored_advert_takes_skills_here_and_text_only_at_the_source()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var source = await SetUp(browser, "greenhouse", $"acme{Guid.NewGuid():N}"[..20]);
        var id = source.GetProperty("id").GetGuid();

        _board.Next = new SourceRead.Adverts([Advert("1", $"Welder {source.GetProperty("code").GetString()}")]);
        await Fetch(browser, id);
        var job = (await Mine(browser)).Single();
        var jobId = job.GetProperty("id").GetGuid();
        var title = job.GetProperty("title").GetString()!;

        object Body(string withTitle) => new
        {
            title = withTitle,
            description = job.GetProperty("description").GetString(),
            location = "Berlin",
            postal_code = "10115",
            remote_mode = "none",
            employment_type = "full_time",
            skills = new[] { "MIG/MAG" }
        };

        var skills = await browser.PutAsJsonAsync($"/jobs/{jobId}", Body(title));
        skills.StatusCode.Should().Be(HttpStatusCode.OK, await skills.Content.ReadAsStringAsync());
        (await Json(skills)).GetProperty("skills").EnumerateArray().Select(skill => skill.GetString())
            .Should().Equal("MIG/MAG");

        var text = await browser.PutAsJsonAsync($"/jobs/{jobId}", Body("Senior Welder"));
        text.StatusCode.Should().Be(HttpStatusCode.Conflict, "the title is changed at the source");

        // Removing the source hands the advert over to the company.
        (await browser.DeleteAsync($"/jobs/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var own = await browser.PutAsJsonAsync($"/jobs/{jobId}", Body("Senior Welder"));
        own.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(own)).TryGetProperty("mirrored", out _).Should().BeFalse();
    }

    /// <summary>A feed is proven by lying on the company's proven domain.</summary>
    [Fact]
    public async Task A_feed_lies_on_the_companys_own_domain()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);
        _domains.Domain = "acme.example";

        var foreign = await browser.PostAsJsonAsync("/jobs/sources", new { kind = "feed", key = "https://other.example/jobs.json" });
        foreign.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        _domains.Asked.Should().Be(company, "the domain is the acting company's, out of the token");

        var own = await SetUp(browser, "feed", "https://careers.acme.example/jobs.json");
        own.GetProperty("proven_at").ValueKind.Should().Be(JsonValueKind.String);
        own.TryGetProperty("code", out _).Should().BeFalse();

        // And the egress boundary has learned the domain — nothing else.
        var egress = _service.Services.GetRequiredService<IEgressPolicy>();
        // Widened twice, through Noelia's interface each time: the company feeds
        // inside, the companies' own model servers around them (ADR-0051).
        egress.Should().BeOfType<CompanyAiEgress>("the widened policy must be the one Noelia's guard resolves")
            .Which.Inner.Should().BeOfType<CompanyDomainEgress>();
        egress.IsAllowed(new Uri("https://careers.acme.example/jobs.json")).Should().BeTrue();
        egress.IsAllowed(new Uri("https://other.example/jobs.json")).Should().BeFalse();
        egress.IsAllowed(new Uri("https://acme.jobs.personio.de/xml")).Should().BeTrue();
    }

    /// <summary>identity-service silent: 503, never a guess about the domain.</summary>
    [Fact]
    public async Task A_silent_identity_service_answers_503()
    {
        _domains.Silent = true;

        var answer = await AsCompany(Guid.CreateVersion7())
            .PostAsJsonAsync("/jobs/sources", new { kind = "feed", key = "https://acme.example/jobs.json" });

        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Theory]
    [InlineData("workday", "acme", "draft")]
    [InlineData("greenhouse", "acme", "now")]
    [InlineData("greenhouse", "acme/../x", "draft")]
    public async Task A_source_outside_the_rules_is_422(string kind, string key, string mode)
    {
        var answer = await AsCompany(Guid.CreateVersion7()).PostAsJsonAsync("/jobs/sources", new { kind, key, mode });

        answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Reading is every member's; setting up, fetching and removing an admin's.</summary>
    [Fact]
    public async Task Only_an_administrator_sets_up_fetches_or_removes_a_source()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var source = await SetUp(browser, "lever", $"acme{Guid.NewGuid():N}"[..20]);
        var id = source.GetProperty("id").GetGuid();

        _roles.Answer = CompanyRole.Member;
        (await browser.GetAsync("/jobs/sources")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.PostAsJsonAsync("/jobs/sources", new { kind = "lever", key = "other" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await browser.PostAsync($"/jobs/sources/{id}/fetch", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await browser.PutAsJsonAsync($"/jobs/sources/{id}", new { mode = "direct" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await browser.DeleteAsync($"/jobs/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Counter-probe: the same calls as admin go through.
        _roles.Answer = CompanyRole.Admin;
        (await browser.PutAsJsonAsync($"/jobs/sources/{id}", new { mode = "direct" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.DeleteAsync($"/jobs/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>A person has no sources; a foreign company's are "no such source".</summary>
    [Fact]
    public async Task Sources_belong_to_their_company()
    {
        var owner = AsCompany(Guid.CreateVersion7());
        var stranger = AsCompany(Guid.CreateVersion7());
        var id = (await SetUp(owner, "greenhouse", $"acme{Guid.NewGuid():N}"[..20])).GetProperty("id").GetGuid();

        (await Json(await stranger.GetAsync("/jobs/sources"))).EnumerateArray().Should().BeEmpty();
        (await stranger.PostAsync($"/jobs/sources/{id}/fetch", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.PutAsJsonAsync($"/jobs/sources/{id}", new { mode = "direct" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.DeleteAsync($"/jobs/sources/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await With(TokenShape.Person(Guid.CreateVersion7())).GetAsync("/jobs/sources"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _service.CreateClient().GetAsync("/jobs/sources")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>The daily round reads only proven sources — an unproven one waits for a click.</summary>
    [Fact]
    public async Task The_daily_round_skips_unproven_boards()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var unproven = await SetUp(browser, "greenhouse", $"acme{Guid.NewGuid():N}"[..20]);
        var schedule = _service.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<SourceSchedule>().Single();

        _board.Read.Clear();
        _board.Next = new SourceRead.Adverts([]);
        await schedule.RunAsync(CancellationToken.None);

        _board.Read.Should().NotContain(unproven.GetProperty("id").GetGuid());
    }

    /// <summary>Answers with what the test set, and remembers which sources were read.</summary>
    private sealed class BoardProbe : ISourceReader
    {
        public SourceRead Next { get; set; } = new SourceRead.Adverts([]);

        public List<Guid> Read { get; } = [];

        public Task<SourceRead> ReadAsync(JobSource source, CancellationToken cancellationToken = default)
        {
            Read.Add(source.Id);
            return Task.FromResult(Next);
        }
    }

    /// <summary>identity-service's domain lookup, remembering whom it was asked about.</summary>
    private sealed class DomainProbe : ICompanyDomains
    {
        public string? Domain { get; set; } = "acme.example";

        public bool Silent { get; set; }

        public Guid? Asked { get; private set; }

        public Task<string?> DomainAsync(TenantId company, CancellationToken cancellationToken = default)
        {
            Asked = company.Value;
            return Silent ? throw new SourceSilent("The probe is silent.") : Task.FromResult(Domain);
        }
    }
}
