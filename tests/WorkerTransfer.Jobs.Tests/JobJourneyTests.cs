using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>A drafter that writes down instead of asking.</summary>
public sealed class ProbeDrafter : IDrafter
{
    /// <summary>What would last have gone out — that is the interesting question.</summary>
    public AdvertDraft? Last { get; private set; }

    public Task<string> DraftAsync(
        AdvertDraft draft, CancellationToken cancellationToken = default)
    {
        Last = draft;
        return Task.FromResult("We are looking for somebody for distributed systems.");
    }
}

/// <summary>Write, publish, find, withdraw.</summary>
[Collection(PostgresCollection.Name)]
public class JobJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string WithdrawalSecret = "withdrawal-secret";

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeDrafter _drafter = new();
    private readonly RoleProbe _roles = new();

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
            host.UseSetting("Erasure:Secret", WithdrawalSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _drafter));
                // The wire to identity-service. The default is `admin`, so the
                // existing journeys go on measuring what they are there for;
                // `RoleTests` turns it to `member` and checks the role.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _roles));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company) =>
        With(TokenShape.Company(Guid.CreateVersion7(), company));

    private HttpClient AsPerson() => With(TokenShape.Person(Guid.CreateVersion7()));

    /// <summary>Without a token — the way an anonymous visitor asks.</summary>
    private HttpClient Anonymous() => _service.CreateClient();

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
        HttpClient browser, string title = "Developer", params string[] skills) =>
        browser.PostAsJsonAsync("/jobs", new
        {
            title,
            description = "We build distributed systems.",
            location = "Berlin",
            remote_mode = "hybrid",
            employment_type = "full_time",
            skills = skills.Length > 0 ? skills : ["C#", "Postgres"]
        });

    private async Task<Guid> Published(HttpClient browser, params string[] skills)
    {
        var created = await Write(browser, skills: skills);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        (await browser.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    /// <summary>The salary range travels in and out, and is read by everyone (B5).</summary>
    [Fact]
    public async Task A_stated_salary_range_is_published_with_the_advert()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);

        var created = await browser.PostAsJsonAsync("/jobs", new
        {
            title = "Welder",
            description = "TIG and MIG.",
            salary = new { min = 42_000, max = 52_000, period = "year" }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await Json(created)).GetProperty("id").GetGuid();
        await browser.PostAsync($"/jobs/{id}/publish", null);

        var read = await Json(await Anonymous().GetAsync($"/jobs/{id}"));
        var salary = read.GetProperty("salary");
        salary.GetProperty("min").GetInt32().Should().Be(42_000);
        salary.GetProperty("max").GetInt32().Should().Be(52_000);
        salary.GetProperty("period").GetString().Should().Be("year");
        salary.GetProperty("currency").GetString().Should().Be("EUR");
    }

    /// <summary>Not stated is a statement too — <c>null</c>, never a made-up figure.</summary>
    [Fact]
    public async Task Without_a_range_the_advert_says_null()
    {
        var created = await Write(AsCompany(Guid.CreateVersion7()));

        (await Json(created)).GetProperty("salary").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task One_end_alone_is_a_422()
    {
        var refused = await AsCompany(Guid.CreateVersion7()).PostAsJsonAsync("/jobs", new
        {
            title = "Welder",
            description = "TIG and MIG.",
            salary = new { min = 42_000 }
        });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task A_change_can_remove_the_range_again()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var created = await browser.PostAsJsonAsync("/jobs", new
        {
            title = "Welder",
            description = "TIG and MIG.",
            salary = new { min = 3_000, max = 3_400, period = "month" }
        });
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var changed = await browser.PutAsJsonAsync($"/jobs/{id}", new { title = "Welder", description = "TIG and MIG." });

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(changed)).GetProperty("salary").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Whoever writes an advert should be able to read it before applications
    /// come in on it.
    /// </summary>
    [Fact]
    public async Task A_new_advert_is_a_draft_and_not_visible()
    {
        var company = Guid.CreateVersion7();
        var created = await Write(AsCompany(company));

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(created)).GetProperty("status").GetString().Should().Be("draft");

        var id = (await Json(created)).GetProperty("id").GetGuid();
        var publicList = await Json(await AsPerson().GetAsync("/jobs"));

        publicList.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Otherwise the service would betray that a company is writing something
    /// right now.
    /// </summary>
    [Fact]
    public async Task A_foreign_draft_looks_like_no_advert()
    {
        var created = await Write(AsCompany(Guid.CreateVersion7()));
        var id = (await Json(created)).GetProperty("id").GetGuid();
        var invented = Guid.CreateVersion7();

        var browser = AsCompany(Guid.CreateVersion7());

        var forDraft = await browser.GetAsync($"/jobs/{id}");
        var forInvented = await browser.GetAsync($"/jobs/{invented}");

        forDraft.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forInvented.StatusCode.Should().Be(forDraft.StatusCode);
    }

    [Fact]
    public async Task Publishing_makes_it_findable()
    {
        var company = Guid.CreateVersion7();
        var id = await Published(AsCompany(company));

        var found = await Json(await AsPerson().GetAsync("/jobs"));

        found.GetProperty("items").EnumerateArray()
            .Should().Contain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// A published advert is found even by somebody who has no account.
    /// </summary>
    /// <remarks>
    /// <para>A lock on <c>principal.Current is null</c> stood here, and it was a
    /// cost without a return: the store filters on <c>status == "published"</c>
    /// anyway, and published means "visible to everybody, even without an
    /// account" in this domain.</para>
    ///
    /// <para>The careers page paid for it. <c>/careers/&lt;slug&gt;</c> is
    /// described as public in <c>docs/routenkarte.yml</c> expressly and still
    /// showed an anonymous visitor not a single job — the company's name and
    /// description yes, its adverts no.</para>
    /// </remarks>
    [Fact]
    public async Task A_published_advert_is_seen_even_without_an_account()
    {
        var id = await Published(AsCompany(Guid.CreateVersion7()));

        var response = await Anonymous().GetAsync("/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("items").EnumerateArray()
            .Should().Contain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>A draft stays inside for an anonymous caller too.</summary>
    /// <remarks>
    /// The counter-probe to the opening. The state is filtered in the store and
    /// never by the caller — a <c>status=draft</c> on the wire would otherwise
    /// be the way to other companies' drafts.
    /// </remarks>
    [Fact]
    public async Task A_draft_stays_hidden_without_an_account_too()
    {
        var created = await Write(AsCompany(Guid.CreateVersion7()), "Secret draft");
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var found = await Json(await Anonymous().GetAsync("/jobs"));

        found.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// A single advert is read even by somebody who has no account — and a draft
    /// stays indistinguishable from "does not exist".
    /// </summary>
    /// <remarks>
    /// The endpoint was behind sign-in, and that cost two things. The careers
    /// page is an address to pass on: whoever opens it has no account, and a 401
    /// turned the advert into a sign-in prompt. And <c>applications-service</c>
    /// asks here service-to-service whether the job exists — without a token,
    /// because it has none. Measured: <b>every</b> application ended with 503,
    /// and the log said "jobs-service answered with 401".
    /// <para>
    /// The second part is the counter-probe to the opening and the more
    /// important one: a draft must not answer 403, because a 403 beside a 404
    /// says "there is something here, you just may not" — and with that one
    /// could query which ids are taken.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_advert_is_read_even_without_an_account()
    {
        var company = Guid.CreateVersion7();
        var draft = (await Json(await Write(AsCompany(company), "Secret draft")))
            .GetProperty("id").GetGuid();
        var open = (await Json(await Write(AsCompany(company), "Open position")))
            .GetProperty("id").GetGuid();
        (await AsCompany(company).PostAsync($"/jobs/{open}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var visible = await Anonymous().GetAsync($"/jobs/{open}");
        var forDraft = await Anonymous().GetAsync($"/jobs/{draft}");
        var forInvented = await Anonymous().GetAsync($"/jobs/{Guid.CreateVersion7()}");

        visible.StatusCode.Should().Be(
            HttpStatusCode.OK, "a published advert is public");
        (await Json(visible)).GetProperty("title").GetString().Should().Be("Open position");

        forDraft.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forInvented.StatusCode.Should().Be(
            forDraft.StatusCode,
            "a draft and an invented id must answer the same");
    }

    /// <summary>
    /// A search term containing an SQL word is answered and not refused.
    /// </summary>
    /// <remarks>
    /// The occasion is Noelia's <c>UseInputSanitization()</c>. It hit the
    /// <b>bare</b> keyword at a word boundary, and a hyphen is one:
    /// <c>Union-Investment</c> — a real German fund company — came back as
    /// <b>400</b>, as did <c>Select-Kundenberater</c> and
    /// <c>Drop-In-Zentrum</c>. On a job board that is no edge case but an
    /// ordinary search that fails without explanation.
    /// <para>
    /// The middleware was therefore removed from <c>ServiceFoundation.cs</c>
    /// (<c>bugs/eingabepruefung-weist-gewoehnliche-woerter-ab.md</c>). This test
    /// is the reason it shows when somebody brings it back: then a red line with
    /// a company name in it stands here, instead of a search that silently does
    /// not work in operation.
    /// </para>
    /// <para>
    /// The <b>status code</b> is checked and not the result: that nothing is
    /// found is fine — that the question may not be asked is not.
    /// </para>
    /// <para>
    /// The terms stay German on purpose: they are the real-world words that
    /// were refused.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Union-Investment")]
    [InlineData("Select-Kundenberater")]
    [InlineData("Drop-In-Zentrum")]
    [InlineData("Update-Managerin")]
    public async Task A_search_term_containing_an_SQL_word_is_answered(string term)
    {
        var response = await Anonymous().GetAsync(
            $"/jobs?q={Uri.EscapeDataString(term)}");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "\"{0}\" is a search term and no injection", term);
    }

    /// <summary>
    /// <c>company</c> really filters — otherwise the careers page shows foreign
    /// adverts under one's own name.
    /// </summary>
    /// <remarks>
    /// The interface had <strong>always</strong> sent the parameter and the
    /// service never read it. Without this test that does not stand out: the
    /// list is not empty, it is merely wrong — and a careers page with foreign
    /// jobs looks like a full one.
    /// </remarks>
    [Fact]
    public async Task The_company_filter_shows_only_its_own_adverts()
    {
        var mine = Guid.CreateVersion7();
        var foreign = Guid.CreateVersion7();

        var mineId = await Published(AsCompany(mine));
        var foreignId = await Published(AsCompany(foreign));

        var found = await Json(await Anonymous().GetAsync($"/jobs?company={mine}"));
        var entries = found.GetProperty("items").EnumerateArray().ToList();

        entries.Should().Contain(entry => entry.GetProperty("id").GetGuid() == mineId);
        entries.Should().NotContain(entry => entry.GetProperty("id").GetGuid() == foreignId);
    }

    /// <summary>The search term searches — over title and description.</summary>
    /// <remarks>
    /// <c>q</c> too was sent and never read: the search form was decoration. It
    /// always delivered everything, which does not stand out as long as few
    /// adverts are in stock.
    /// </remarks>
    [Fact]
    public async Task The_search_term_hits_title_and_description()
    {
        var company = AsCompany(Guid.CreateVersion7());
        var wanted = await Published(company);
        var other = (await Json(await Write(company, "Gardener"))).GetProperty("id").GetGuid();
        (await company.PostAsync($"/jobs/{other}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var found = await Json(await Anonymous().GetAsync("/jobs?q=developer"));
        var entries = found.GetProperty("items").EnumerateArray().ToList();

        // Searched in lower case, stored with a capital.
        entries.Should().Contain(entry => entry.GetProperty("id").GetGuid() == wanted);
        entries.Should().NotContain(entry => entry.GetProperty("id").GetGuid() == other);
    }

    [Fact]
    public async Task A_foreign_advert_cannot_be_changed()
    {
        var id = await Published(AsCompany(Guid.CreateVersion7()));

        var attempt = await AsCompany(Guid.CreateVersion7()).PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Taken over", description = "Not my advert."
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Changing a closed advert would mean changing the text people applied to.
    /// </summary>
    [Fact]
    public async Task Nobody_changes_a_closed_advert_any_more()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var id = await Published(browser);

        await browser.PostAsync($"/jobs/{id}/close", null);

        var attempt = await browser.PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Different after all", description = "Afterwards."
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_private_person_writes_no_advert()
    {
        var attempt = await Write(AsPerson());

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Rename first, then de-duplicate (ADR-0023). The other way round the
    /// advert would count one requirement double.
    /// </summary>
    [Fact]
    public async Task Postgres_and_PostgreSQL_are_one_requirement()
    {
        var created = await Write(
            AsCompany(Guid.CreateVersion7()), skills: ["Postgres", "PostgreSQL", "C#"]);

        var skills = (await Json(created)).GetProperty("skills");

        skills.GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// A statement about language is allowed; one about a human being is not.
    /// "React" does not mean that somebody knows JavaScript.
    /// </summary>
    [Fact]
    public async Task React_does_not_become_JavaScript()
    {
        var created = await Write(AsCompany(Guid.CreateVersion7()), skills: ["React"]);

        var skills = (await Json(created)).GetProperty("skills")
            .EnumerateArray().Select(entry => entry.GetString()).ToList();

        skills.Should().ContainSingle();
        skills.Should().NotContain("JavaScript");
    }

    /// <summary>
    /// A requirement longer than a person can enter at all would be guaranteed
    /// never to be a hit — a line that can never become a tick for anybody.
    /// </summary>
    [Fact]
    public void A_job_may_not_ask_for_a_skill_no_profile_can_hold()
    {
        Domain.Jobs.SkillList.MaxLength
            .Should().BeLessThanOrEqualTo(
                Profile.Domain.Skills.SkillList.MaxLength);
    }

    [Fact]
    public async Task More_than_twenty_requirements_are_refused()
    {
        var tooMany = Enumerable.Range(0, 21).Select(number => $"Skill{number}").ToArray();

        var attempt = await Write(AsCompany(Guid.CreateVersion7()), skills: tooMany);

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// It helps a company phrase its OWN advert and never says anything about
    /// anybody (ADR-0024).
    /// </summary>
    [Fact]
    public async Task The_draft_carries_neither_tenant_nor_company_name()
    {
        var company = Guid.CreateVersion7();

        var drafted = await AsCompany(company).PostAsJsonAsync("/jobs/draft", new
        {
            title = "Developer",
            description = "We build distributed systems.",
            skills = new[] { "C#" },
            location = "Berlin",
            wish = "shorter"
        });

        drafted.StatusCode.Should().Be(HttpStatusCode.OK);

        _drafter.Last.Should().NotBeNull();
        var prompt = _drafter.Last!.Prompt;

        prompt.Should().NotContain(company.ToString(), "no tenant_id");
        prompt.Should().Contain("shorter");
    }

    /// <summary>
    /// The wish is limited — and one that is too long is input, not an outage.
    /// </summary>
    /// <remarks>
    /// It was the only field of this endpoint that passes no value object:
    /// title, description, location and skills have long been limited in the
    /// domain, the wish came raw from the body and went unchecked to the foreign
    /// provider. Both are checked — that 501 characters are refused, and that
    /// 500 pass: a limit that also refuses what is allowed is noticed only at a
    /// real request.
    /// </remarks>
    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.UnprocessableEntity)]
    public async Task A_wish_that_is_too_long_is_a_422(int length, HttpStatusCode expected)
    {
        var response = await AsCompany(Guid.CreateVersion7()).PostAsJsonAsync("/jobs/draft", new
        {
            title = "Developer",
            description = "We build distributed systems.",
            skills = new[] { "C#" },
            location = "Berlin",
            wish = new string('a', length)
        });

        response.StatusCode.Should().Be(expected);
    }

    /// <summary>
    /// No statements about applicants — the rules say so expressly, because an
    /// advert is the place where such a thing otherwise slips in.
    /// </summary>
    /// <remarks>
    /// The rules are what the model receives and stay German, so the checked
    /// phrases are German too.
    /// </remarks>
    [Fact]
    public void The_draft_rules_forbid_statements_about_people()
    {
        AdvertDraft.Rules.Should().Contain("Erfinde NICHTS hinzu");
        AdvertDraft.Rules.Should().Contain("Keine Aussagen über Bewerberinnen");
    }

    /// <summary>
    /// It holds nothing about a natural person (ADR-0027 §2). An erasure command
    /// to it would be an endpoint that says "done" without ever doing anything.
    /// </summary>
    [Fact]
    public async Task There_is_no_erasure_endpoint()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        request.Headers.Add("X-Erasure-Secret", WithdrawalSecret);

        var attempt = await _service.CreateClient().SendAsync(request);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Withdrawn, not erased: a company is no natural person, and its adverts
    /// still belong to it then. But an unsupervised advert is worse than none.
    /// </summary>
    [Fact]
    public async Task The_company_withdrawal_closes_all_open_adverts()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);

        await Published(browser);
        await Write(browser, "A draft");

        var request = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = company })
        };
        request.Headers.Add("X-Erasure-Secret", WithdrawalSecret);

        var withdrawn = await _service.CreateClient().SendAsync(request);

        withdrawn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(withdrawn)).GetProperty("withdrawn").GetInt32().Should().Be(2);

        var mine = await Json(await browser.GetAsync("/companies/me/jobs"));

        mine.EnumerateArray()
            .Should().OnlyContain(entry => entry.GetProperty("status").GetString() == "closed");
    }

    [Fact]
    public async Task Without_the_right_secret_nothing_is_withdrawn()
    {
        var company = Guid.CreateVersion7();
        await Published(AsCompany(company));

        var request = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = company })
        };
        request.Headers.Add("X-Erasure-Secret", "wrong");

        var attempt = await _service.CreateClient().SendAsync(request);

        attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// No score, no rank, no percentage — the fit is computed by the browser and
    /// shown to the person (ADR-0022).
    /// </summary>
    [Fact]
    public async Task No_advert_carries_a_score()
    {
        await Published(AsCompany(Guid.CreateVersion7()));

        var body = await (await AsPerson().GetAsync("/jobs")).Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "score", "rank", "match", "percent", "weight" })
        {
            body.Should().NotContain(forbidden);
        }
    }

    /// <summary>
    /// The state is filtered in the store, not by the caller: a
    /// <c>status=draft</c> on the wire would be a way to read other companies'
    /// drafts.
    /// </summary>
    [Fact]
    public async Task A_state_parameter_opens_no_foreign_drafts()
    {
        var created = await Write(AsCompany(Guid.CreateVersion7()));
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var page = await Json(await AsPerson().GetAsync("/jobs?status=draft"));

        page.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }
}
