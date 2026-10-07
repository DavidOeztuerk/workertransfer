using WorkerTransfer.ServiceDefaults.Roles;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Resume.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Resume.Tests;

/// <summary>Writing a résumé, asking for one, answering, taking it back.</summary>
[Collection(PostgresCollection.Name)]
public class ResumeJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeGate _gate = new();
    private readonly ProbeDelivery _delivery = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:resume", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _gate));
                services.Replace(ServiceDescriptor.Scoped<IDelivery>(_ => _delivery));
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

    private readonly RoleProbe _roles = new();

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient AsCompany(Guid subject, Guid company) =>
        With(TokenShape.Company(subject, company));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(HttpClient browser) =>
        browser.PutAsJsonAsync("/resumes/me", new
        {
            positions = new[]
            {
                new
                {
                    employer = "Example Ltd",
                    title = "Developer",
                    started_on = "2020-03",
                    ended_on = (string?)null,
                    description = "Backend"
                }
            },
            education = new[]
            {
                new
                {
                    institution = "Example University",
                    qualification = "B.Sc.",
                    started_on = "2015-10",
                    ended_on = "2019-09"
                }
            }
        });

    /// <summary>
    /// Technologies on a position: <strong>canonicalised and deduplicated</strong>.
    /// </summary>
    /// <remarks>
    /// The same vocabulary as on the profile — "postgres" becomes "PostgreSQL"
    /// here as there (ADR-0023). Canonicalise first, then deduplicate: the other
    /// way round both spellings would stand as two entries and only afterwards
    /// both become "PostgreSQL".
    /// <para>
    /// The reason the field exists at all: it is the second place where
    /// somebody can NAME what they can do. That does not become searchable — a
    /// résumé is released one company at a time (ADR-0020) — but on the profile
    /// it becomes a naming with one click, and that is searchable.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Technologies_are_canonicalised_and_deduplicated()
    {
        var browser = AsPerson(Guid.CreateVersion7());

        var written = await browser.PutAsJsonAsync("/resumes/me", new
        {
            positions = new[]
            {
                new
                {
                    employer = "Example Ltd",
                    title = "Developer",
                    started_on = "2020-03",
                    ended_on = (string?)null,
                    description = string.Empty,
                    technologies = new[] { "postgres", "PostgreSQL", " Go ", string.Empty }
                }
            },
            education = Array.Empty<object>()
        });

        written.StatusCode.Should().Be(HttpStatusCode.OK);

        var position = (await Json(written)).GetProperty("positions")[0];

        position.GetProperty("technologies").EnumerateArray()
            .Select(entry => entry.GetString())
            .Should().Equal("PostgreSQL", "Go");
    }

    /// <summary>More than twelve say nothing about a single job any more.</summary>
    [Fact]
    public async Task Too_many_technologies_are_refused()
    {
        var browser = AsPerson(Guid.CreateVersion7());

        var response = await browser.PutAsJsonAsync("/resumes/me", new
        {
            positions = new[]
            {
                new
                {
                    employer = "Example Ltd",
                    title = "Developer",
                    started_on = "2020-03",
                    ended_on = (string?)null,
                    description = string.Empty,
                    technologies = Enumerable.Range(1, 13).Select(i => $"Tool{i}").ToArray()
                }
            },
            education = Array.Empty<object>()
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>A position without technologies is not an error, but empty.</summary>
    /// <remarks>
    /// Older rows do not carry the field at all — they must stay readable
    /// without anybody running a data migration.
    /// </remarks>
    [Fact]
    public async Task Without_technologies_the_list_stays_empty()
    {
        var browser = AsPerson(Guid.CreateVersion7());

        var written = await Write(browser);
        var position = (await Json(written)).GetProperty("positions")[0];

        position.GetProperty("technologies").GetArrayLength().Should().Be(0);
    }

    /// <summary>The whole way: write, be asked, say yes, be read, take it back.</summary>
    [Fact]
    public async Task Write_be_asked_grant_be_read_revoke()
    {
        var anna = Guid.CreateVersion7();
        var manager = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();

        var hers = AsPerson(anna);
        (await Write(hers)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Asking needs the PROFILE release, never the existence of a résumé.
        _gate.ProfileReleased.Add(anna);

        var asked = await AsCompany(manager, company)
            .PostAsync($"/resumes/{anna}/requests", null);
        asked.StatusCode.Should().Be(HttpStatusCode.Created);

        var id = (await Json(asked)).GetProperty("id").GetGuid();

        (await hers.PostAsync($"/resumes/requests/{id}/grant", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var read = await AsCompany(manager, company).GetAsync($"/resumes/{anna}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        (await read.Content.ReadAsStringAsync()).Should().Contain("Example Ltd");

        (await hers.PostAsync($"/resumes/requests/{id}/revoke", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AsCompany(manager, company).GetAsync($"/resumes/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// <c>GRANTED</c> means "was granted once", not "holds now". After a
    /// withdrawal the request stays granted and the read comes up empty — that
    /// is the design, not a bug to fix.
    /// </summary>
    [Fact]
    public async Task After_the_revocation_the_request_stays_granted_and_the_read_empty()
    {
        var anna = Guid.CreateVersion7();
        var manager = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();

        var hers = AsPerson(anna);
        await Write(hers);
        _gate.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(manager, company)
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        await hers.PostAsync($"/resumes/requests/{id}/grant", null);
        await hers.PostAsync($"/resumes/requests/{id}/revoke", null);

        var mine = await Json(await hers.GetAsync("/resumes/me/requests"));

        mine[0].GetProperty("status").GetString().Should().Be(
            "GRANTED", "it was granted once — that stays true");
        mine[0].GetProperty("active").GetBoolean().Should().BeFalse(
            "it no longer holds now — and that is the other question");

        (await AsCompany(manager, company).GetAsync($"/resumes/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// "Has already written one" is a fact about the person that nobody should
    /// be able to probe for — so asking requires only the profile release.
    /// </summary>
    [Fact]
    public async Task Asking_works_without_a_resume_existing()
    {
        var anna = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        var asked = await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null);

        asked.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Without the profile release the company gets the same answer a
    /// non-existent person gets. Anything else would say whether somebody is
    /// here.
    /// </summary>
    [Fact]
    public async Task Without_a_profile_release_nobody_is_there()
    {
        var anna = Guid.CreateVersion7();
        var invented = Guid.CreateVersion7();
        var browser = AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7());

        await Write(AsPerson(anna));

        var forReal = await browser.PostAsync($"/resumes/{anna}/requests", null);
        var forInvented = await browser.PostAsync($"/resumes/{invented}/requests", null);

        forReal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forInvented.StatusCode.Should().Be(forReal.StatusCode);
    }

    /// <summary>Asking for a CV is a first contact: at least a recruiter (ADR-0048).</summary>
    /// <remarks>
    /// Refused BEFORE the person is looked at: a member gets 403 whether or not
    /// she exists, so the refusal says nothing about her.
    /// </remarks>
    [Theory]
    [InlineData(CompanyRole.Member, HttpStatusCode.Forbidden)]
    [InlineData(CompanyRole.Recruiter, HttpStatusCode.NotFound)]
    [InlineData(CompanyRole.Admin, HttpStatusCode.NotFound)]
    public async Task Only_a_recruiter_or_an_admin_asks_for_a_cv(CompanyRole role, HttpStatusCode expected)
    {
        _roles.Answer = role;

        var asked = await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{Guid.CreateVersion7()}/requests", null);

        asked.StatusCode.Should().Be(expected);
    }

    /// <summary>A silent role lookup is 503, never 403.</summary>
    [Fact]
    public async Task A_silent_role_lookup_is_503()
    {
        _roles.Silent = true;

        (await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
                .PostAsync($"/resumes/{Guid.CreateVersion7()}/requests", null))
            .StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>There is no public switch, so a private person reads nothing.</summary>
    [Fact]
    public async Task A_private_person_reads_no_foreign_resume()
    {
        var anna = Guid.CreateVersion7();
        await Write(AsPerson(anna));

        var attempt = await AsPerson(Guid.CreateVersion7()).GetAsync($"/resumes/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A withdrawal has to take effect on the very next read, so the gate is
    /// asked every single time and nothing is remembered between requests.
    /// </summary>
    [Fact]
    public async Task The_ledger_is_asked_anew_on_every_read()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.ResumeReleased.Add((anna, company));

        var browser = AsCompany(Guid.CreateVersion7(), company);
        await browser.GetAsync($"/resumes/{anna}");
        var afterFirst = _gate.Questions;

        await browser.GetAsync($"/resumes/{anna}");

        _gate.Questions.Should().Be(afterFirst + 1, "nothing is cached");
    }

    /// <summary>
    /// Neither a 404 nor the résumé: both would assert something nobody knows.
    /// </summary>
    [Fact]
    public async Task If_the_ledger_is_silent_nothing_is_asserted()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Write(AsPerson(anna));
        _gate.ResumeReleased.Add((anna, company));
        _gate.Silent = true;

        var attempt = await AsCompany(Guid.CreateVersion7(), company).GetAsync($"/resumes/{anna}");

        attempt.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Only the person asked may answer — and a stranger gets the same 404 a
    /// made-up id gets.
    /// </summary>
    /// <remarks>
    /// Not 403. A foreign request id behaves like a foreign subject id: "not
    /// there" and "not mine" must look the same from outside, or the endpoint
    /// confirms that a particular company asked a particular person.
    /// </remarks>
    [Fact]
    public async Task A_stranger_does_not_answer_the_request()
    {
        var anna = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        var invented = Guid.CreateVersion7();

        var byStranger = await AsPerson(Guid.CreateVersion7())
            .PostAsync($"/resumes/requests/{id}/grant", null);
        var forInvented = await AsPerson(Guid.CreateVersion7())
            .PostAsync($"/resumes/requests/{invented}/grant", null);

        byStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forInvented.StatusCode.Should().Be(byStranger.StatusCode);
    }

    [Fact]
    public async Task The_same_company_does_not_ask_twice()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        var browser = AsCompany(Guid.CreateVersion7(), company);
        await browser.PostAsync($"/resumes/{anna}/requests", null);

        var second = await browser.PostAsync($"/resumes/{anna}/requests", null);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A grant and a refusal answer a question the company itself asked, and it
    /// is entitled to hear the answer.
    /// </summary>
    [Fact]
    public async Task Questions_and_answers_land_in_the_outbox()
    {
        var anna = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        await AsPerson(anna).PostAsync($"/resumes/requests/{id}/grant", null);

        await using var context = Context();
        var kinds = context.Set<OutboxRow>().Select(row => row.Kind).ToList();

        kinds.Should().Contain("resume.requested");
        kinds.Should().Contain("resume.granted");
    }

    /// <summary>
    /// There is deliberately no kind for a withdrawal. Pushing it at the
    /// company would turn taking a release back into a confrontation, and the
    /// whole point of reading the ledger fresh is that withdrawing costs the
    /// person nothing.
    /// </summary>
    [Fact]
    public async Task A_revocation_notifies_nobody()
    {
        var anna = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null))).GetProperty("id").GetGuid();

        var hers = AsPerson(anna);
        await hers.PostAsync($"/resumes/requests/{id}/grant", null);

        await using var before = Context();
        var rowsBefore = before.Set<OutboxRow>().Count();

        await hers.PostAsync($"/resumes/requests/{id}/revoke", null);

        await using var after = Context();
        after.Set<OutboxRow>().Count().Should().Be(rowsBefore);
    }

    /// <summary>
    /// The intent commits with the domain change. Rolled back, it is gone too —
    /// so nobody is told their request was answered when it was not.
    /// </summary>
    [Fact]
    public async Task The_intent_lies_beside_the_change_in_the_same_database()
    {
        var anna = Guid.CreateVersion7();
        _gate.ProfileReleased.Add(anna);

        await AsCompany(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsync($"/resumes/{anna}/requests", null);

        await using var context = Context();

        context.Requests.Count(row => row.SubjectId == anna).Should().Be(1);
        context.Set<OutboxRow>().Count(row => row.UserId == anna).Should().Be(1);
    }

    private Infrastructure.Persistence.ResumeDbContext Context()
    {
        var source = Infrastructure.Persistence.ResumeDbContextFactory
            .DataSource(postgres.ConnectionString);

        return new Infrastructure.Persistence.ResumeDbContext(
            (Microsoft.EntityFrameworkCore.DbContextOptions<
                Infrastructure.Persistence.ResumeDbContext>)
            Infrastructure.Persistence.ResumeDbContextFactory.Configure(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                    Infrastructure.Persistence.ResumeDbContext>(), source).Options);
    }
}
