using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Profile.Domain.Audit;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Profile.Application.Ports;
using WorkerTransfer.Profile.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Profile.Tests;

/// <summary>A ledger the test steers.</summary>
/// <remarks>
/// The real one does not run here. What is checked is not how it answers but
/// what <em>this</em> service does with the answer — that it asks at all, at
/// every read, and that a silent ledger is neither a yes nor a no.
/// </remarks>
public sealed class ProbeGate : IConsentGate
{
    /// <summary>Who is visible to which company.</summary>
    public HashSet<(Guid Subject, Guid Company)> Released { get; } = [];

    /// <summary>How often it was asked. The read path must ask every time.</summary>
    public int Questions { get; private set; }

    /// <summary>If true, the ledger says nothing at all.</summary>
    public bool Silent { get; set; }

    public Task<bool> MaySeeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        Questions++;
        return Silent
            ? throw new ConsentGateSilent("the ledger does not answer in the test")
            : Task.FromResult(Released.Contains((subject.Value, company.Value)));
    }
}

/// <summary>A drafter that records instead of asking.</summary>
public sealed class ProbeDrafter : IDrafter
{
    /// <summary>What would last have gone out — that is the interesting question.</summary>
    public DraftSituation? Last { get; private set; }

    public Task<string> DraftAsync(
        DraftSituation situation, SubjectId requestedBy, CancellationToken cancellationToken = default)
    {
        Last = situation;
        return Task.FromResult("I work on distributed systems.");
    }
}

/// <summary>Writing, being seen, staying hidden, being erased.</summary>
[Collection(PostgresCollection.Name)]
public class ProfileJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGate _gate = new();
    private readonly ProbeDrafter _drafter = new();

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
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate));
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _drafter));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient AsCompany(Guid subject, Guid company) => With(TokenShape.Company(subject, company));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser, string headline = "Developer") =>
        browser.PutAsJsonAsync("/profiles/me", new
        {
            headline,
            bio = "I work on distributed systems.",
            location = "Berlin",
            remote_ok = true,
            skills = new[] { "C#", "Postgres" }
        });

    [Fact]
    public async Task Writing_and_reading_ones_own_back()
    {
        var anna = Guid.CreateVersion7();
        var browser = AsPerson(anna);

        (await Write(browser)).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await Json(await browser.GetAsync("/profiles/me"));

        mine.GetProperty("headline").GetString().Should().Be("Developer");
        mine.GetProperty("skills").GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Whether a profile may be shown stands exclusively in the ledger. A field
    /// here would be a second truth, and the two would disagree at the first
    /// revocation.
    /// </summary>
    [Fact]
    public async Task The_profile_carries_no_visibility_field()
    {
        var anna = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        var body = await (await AsPerson(anna).GetAsync("/profiles/me"))
            .Content.ReadAsStringAsync();

        body.Should().NotContain("visibility");
        body.Should().NotContain("public");
        body.Should().NotContain("is_visible");
    }

    /// <summary>
    /// Hidden and not present must look the same — apart from the correlation
    /// id that stands in every problem document.
    /// </summary>
    [Fact]
    public async Task Hidden_and_not_present_answer_the_same()
    {
        var anna = Guid.CreateVersion7();
        var invented = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        var browser = AsCompany(Guid.CreateVersion7(), company);

        var forHidden = await browser.GetAsync($"/profiles/{anna}");
        var forInvented = await browser.GetAsync($"/profiles/{invented}");

        forHidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forInvented.StatusCode.Should().Be(forHidden.StatusCode);

        WithoutCorrelation(await forHidden.Content.ReadAsStringAsync())
            .Should().Be(WithoutCorrelation(await forInvented.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task With_a_release_the_company_sees_the_profile()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.Released.Add((anna, company));

        var read = await AsCompany(Guid.CreateVersion7(), company).GetAsync($"/profiles/{anna}");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(read)).GetProperty("headline").GetString().Should().Be("Developer");
    }

    /// <summary>
    /// A statement about the caller that betrays nothing about the person they
    /// ask about.
    /// </summary>
    [Fact]
    public async Task Without_an_active_company_there_is_403_and_not_404()
    {
        var anna = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        var attempt = await AsPerson(Guid.CreateVersion7()).GetAsync($"/profiles/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Neither 404 nor the profile: both would claim something nobody knows.
    /// </summary>
    [Fact]
    public async Task If_the_ledger_is_silent_nothing_is_claimed()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.Released.Add((anna, company));
        _gate.Silent = true;

        var attempt = await AsCompany(Guid.CreateVersion7(), company).GetAsync($"/profiles/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// A revocation must take effect at the next read, so it is asked anew
    /// every time and nothing is kept between requests.
    /// </summary>
    [Fact]
    public async Task The_ledger_is_asked_anew_at_every_read()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.Released.Add((anna, company));

        var browser = AsCompany(Guid.CreateVersion7(), company);
        await browser.GetAsync($"/profiles/{anna}");
        var afterFirst = _gate.Questions;

        await browser.GetAsync($"/profiles/{anna}");

        _gate.Questions.Should().Be(afterFirst + 1, "nothing is cached");
    }

    /// <summary>
    /// No points value, no rank, no percentage — about nobody (ADR-0022).
    /// </summary>
    [Fact]
    public async Task Nobody_gets_a_points_value()
    {
        var company = Guid.CreateVersion7();
        var anna = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.Released.Add((anna, company));

        var browser = AsCompany(Guid.CreateVersion7(), company);
        var single = await (await browser.GetAsync($"/profiles/{anna}")).Content.ReadAsStringAsync();
        var own = await (await AsPerson(anna).GetAsync("/profiles/me")).Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "score", "rank", "match", "percent", "weight" })
        {
            single.Should().NotContain(forbidden);
            own.Should().NotContain(forbidden);
        }
    }

    /// <summary>
    /// Commute and relocation travel as their wire words and are stored as the
    /// old member names.
    /// </summary>
    /// <remarks>
    /// <para>Two contracts in one test. The wire words ("bis_50", "offen") are
    /// what <c>web/</c> and scout-service read. The column values ("Bis50",
    /// "Offen") are what every existing row holds: they were the C# member names
    /// while the columns were written with <c>HasConversion&lt;string&gt;()</c>.
    /// The members are English now, and the converters in
    /// <c>ProfileDbContext</c> keep writing the old strings.</para>
    ///
    /// <para><strong>Read with SQL, not through EF</strong>: a round trip
    /// through the same converter would be green whatever it wrote.</para>
    /// </remarks>
    [Fact]
    public async Task Commute_and_relocation_keep_their_stored_words()
    {
        var anna = Guid.CreateVersion7();
        var browser = AsPerson(anna);

        (await browser.PutAsJsonAsync("/profiles/me", new
        {
            headline = "Developer",
            bio = "",
            location = "Berlin",
            remote_ok = false,
            skills = Array.Empty<string>(),
            commute_km = "bis_50",
            relocation = "offen"
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var source = ProfileDbContextFactory.DataSource(postgres.ConnectionString))
        await using (var command = source.CreateCommand(
            "SELECT commute_km, relocation FROM profiles WHERE id = $1"))
        {
            command.Parameters.AddWithValue(anna);

            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();

            reader.GetString(0).Should().Be("Bis50", "the column holds the old member name");
            reader.GetString(1).Should().Be("Offen", "the column holds the old member name");
        }

        var mine = await Json(await browser.GetAsync("/profiles/me"));

        mine.GetProperty("commute_km").GetString().Should().Be("bis_50");
        mine.GetProperty("relocation").GetString().Should().Be("offen");
    }

    /// <summary>
    /// This class IS the boundary: what the person wrote about themselves, and
    /// nothing else.
    /// </summary>
    [Fact]
    public async Task The_draft_carries_nothing_beyond_the_person()
    {
        var anna = Guid.CreateVersion7();
        var browser = AsPerson(anna);
        await Write(browser);

        var drafted = await browser.PostAsJsonAsync(
            "/profiles/me/draft", new { wish = "shorter" });

        drafted.StatusCode.Should().Be(HttpStatusCode.OK);

        _drafter.Last.Should().NotBeNull();
        var prompt = _drafter.Last!.Prompt;

        prompt.Should().NotContain(anna.ToString(), "no SubjectId");
        prompt.Should().NotContain("@", "no address");
        prompt.Should().Contain("shorter", "the wish is the caller's only line");
    }

    /// <summary>
    /// The wish is limited — and one that is too long is an input, not an outage.
    /// </summary>
    /// <remarks>
    /// It was the only field of this endpoint that passes no value object:
    /// headline, text and skills come from the stored profile and are limited
    /// there, the wish came raw from the body and went unchecked to the foreign
    /// provider. Both are checked — 501 refused, 500 let through: a limit that
    /// refuses the allowed as well is noticed only at a real request.
    /// </remarks>
    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.UnprocessableEntity)]
    public async Task A_wish_that_is_too_long_is_a_422(int length, HttpStatusCode expected)
    {
        var browser = AsPerson(Guid.CreateVersion7());
        await Write(browser);

        var response = await browser.PostAsJsonAsync(
            "/profiles/me/draft", new { wish = new string('a', length) });

        response.StatusCode.Should().Be(expected);
    }

    /// <summary>
    /// Nothing is stored — not the prompt, not the answer, and no row saying
    /// that somebody asked for help.
    /// </summary>
    [Fact]
    public async Task The_draft_leaves_no_trail()
    {
        var anna = Guid.CreateVersion7();
        var browser = AsPerson(anna);
        await Write(browser);

        await using (var before = Context())
        {
            var earlier = before.AuditEntries.Count();

            await browser.PostAsJsonAsync("/profiles/me/draft", new { wish = "shorter" });

            await using var after = Context();
            after.AuditEntries.Count().Should().Be(earlier);
        }
    }

    [Fact]
    public async Task The_erasure_takes_the_profile_with_it()
    {
        var anna = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = anna })
        };
        request.Headers.Add("X-Erasure-Secret", ErasureSecret);

        var erased = await _service.CreateClient().SendAsync(request);

        erased.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(erased)).GetProperty("retained").GetInt32().Should().Be(0);

        var after = await AsPerson(anna).GetAsync("/profiles/me");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        (await after.Content.ReadAsStringAsync()).Trim().Should().Be("null");
    }

    /// <summary>The trail says once that a profile was deleted — and never when there was none.</summary>
    /// <remarks>
    /// The cascade delivers at least once. Until 03.10.2026 every delivery
    /// appended "profile deleted", so a second one recorded a second erasure,
    /// and a person without a profile got one recorded that never happened.
    /// </remarks>
    [Fact]
    public async Task The_trail_records_a_deleted_profile_once_and_a_missing_one_never()
    {
        var anna = Guid.CreateVersion7();
        var nobody = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        foreach (var subject in new[] { anna, anna, nobody })
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
            {
                Content = JsonContent.Create(new { user_id = subject })
            };
            request.Headers.Add("X-Erasure-Secret", ErasureSecret);

            (await _service.CreateClient().SendAsync(request))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var context = Context();

        context.AuditEntries.Count(row =>
                row.TargetId == anna && row.Action == AuditAction.ProfileDeleted)
            .Should().Be(1, "two deliveries are one erasure");
        context.AuditEntries.Count(row => row.TargetId == nobody)
            .Should().Be(0, "there was no profile to delete");
    }

    [Fact]
    public async Task An_empty_erasure_secret_closes_the_endpoint()
    {
        using var without = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:profile", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", string.Empty);
            host.UseSetting("environment", "Development");
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        request.Headers.Add("X-Erasure-Secret", "anything");

        (await without.CreateClient().SendAsync(request)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    private static object WithXing(string? xing) => new
    {
        headline = "Welder",
        bio = "",
        location = "Leipzig",
        remote_ok = false,
        skills = Array.Empty<string>(),
        xing_url = xing
    };

    /// <summary>
    /// The XING link (VISION-FRAGEN-2 N2) is stored in one shape, and the form
    /// sending none takes it back.
    /// </summary>
    [Fact]
    public async Task A_xing_link_is_kept_in_one_shape_and_taken_back_by_leaving_it_out()
    {
        var browser = AsPerson(Guid.CreateVersion7());

        (await browser.PutAsJsonAsync("/profiles/me", WithXing("  https://xing.com/profile/Anna_Schmidt7/?sc_o=share  ")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await browser.GetAsync("/profiles/me"))).GetProperty("xing_url").GetString()
            .Should().Be("https://www.xing.com/profile/Anna_Schmidt7");

        (await browser.PutAsJsonAsync("/profiles/me", WithXing(null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(await browser.GetAsync("/profiles/me"))).GetProperty("xing_url").ValueKind
            .Should().Be(JsonValueKind.Null, "a link one cannot take back is no voluntary one");
    }

    /// <summary>Only a XING profile — the page renders it as a link in front of every company.</summary>
    [Theory]
    [InlineData("https://evil.example/profile/Anna")]
    [InlineData("http://www.xing.com/profile/Anna")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://www.xing.com/pages/acme")]
    [InlineData("https://www.xing.com.evil.test/profile/Anna")]
    [InlineData("https://www.xing.com/profile/Anna/../../evil")]
    public async Task Only_a_xing_profile_is_a_xing_link(string address)
    {
        var browser = AsPerson(Guid.CreateVersion7());

        var answer = await browser.PutAsJsonAsync("/profiles/me", WithXing(address));

        answer.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await answer.Content.ReadAsStringAsync()).Should().Contain("XING profile address");
    }

    private static string WithoutCorrelation(string problem) =>
        System.Text.RegularExpressions.Regex.Replace(
            problem, "\"correlationId\":\"[^\"]*\",?", string.Empty,
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));

    private ProfileDbContext Context()
    {
        var source = ProfileDbContextFactory.DataSource(postgres.ConnectionString);

        return new ProfileDbContext(
            (Microsoft.EntityFrameworkCore.DbContextOptions<ProfileDbContext>)
            ProfileDbContextFactory.Configure(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ProfileDbContext>(),
                source).Options);
    }
}
