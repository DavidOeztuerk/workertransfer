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

/// <summary>
/// "admin" is more than a word in a table — at the two places where an advert
/// represents the company to the outside.
/// </summary>
/// <remarks>
/// <para>Until PBI-2 there was <strong>not a single</strong> permission check in
/// the whole tree outside identity-service: the navigation hid company entries,
/// and the server answered 403 only where somebody had thought of it. Hiding is
/// no access control.</para>
///
/// <para><strong>Every test here has its own counter-probe.</strong> An endpoint
/// that ALWAYS answers 403 cannot be told apart from a correctly protected one —
/// so beside every "member gets 403" stands "admin gets 200", in the same test
/// and on the same case.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RoleTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
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
            host.UseSetting("Erasure:Geheimnis", "withdrawal-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => new ProbeDrafter()));
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

    private HttpClient AsCompany(Guid company, Guid? subject = null)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Company(subject ?? Guid.CreateVersion7(), company));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<Guid> Draft(HttpClient browser)
    {
        var created = await browser.PostAsJsonAsync("/jobs", new
        {
            title = "Metalworker",
            description = "We build railings.",
            location = "Berlin",
            remote_mode = "onsite",
            employment_type = "full_time",
            skills = new[] { "MIG/MAG" }
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(created)).GetProperty("id").GetGuid();
    }

    /// <summary>A <c>member</c> puts up no advert.</summary>
    [Fact]
    public async Task Only_an_administrator_publishes()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);
        var id = await Draft(browser);

        _roles.Answer = CompanyRole.Member;
        var asMember = await browser.PostAsync($"/jobs/{id}/publish", null);

        asMember.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "a published advert represents the whole company");

        _roles.Answer = CompanyRole.Admin;
        var asAdmin = await browser.PostAsync($"/jobs/{id}/publish", null);

        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// And closes none — that is the act from the user story.
    /// </summary>
    /// <remarks>
    /// "A <c>member</c> cannot delete my jobs" means exactly this in this
    /// domain: an advert is never deleted, it is closed. And with it the chance
    /// to apply disappears.
    /// </remarks>
    [Fact]
    public async Task Only_an_administrator_closes()
    {
        var company = Guid.CreateVersion7();
        var browser = AsCompany(company);
        var id = await Draft(browser);

        (await browser.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _roles.Answer = CompanyRole.Member;
        var asMember = await browser.PostAsync($"/jobs/{id}/close", null);

        asMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _roles.Answer = CompanyRole.Admin;
        var asAdmin = await browser.PostAsync($"/jobs/{id}/close", null);

        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A <c>member</c> writes and changes adverts — otherwise the invitation
    /// would be a spectator seat.
    /// </summary>
    /// <remarks>
    /// The half that gets lost easily: a permission that is too narrow looks
    /// like security and leads to somebody creating a second admin in order to
    /// work — and then "admin" is a word in a table again.
    /// </remarks>
    [Fact]
    public async Task A_member_writes_and_changes_an_advert()
    {
        _roles.Answer = CompanyRole.Member;

        var browser = AsCompany(Guid.CreateVersion7());
        var id = await Draft(browser);

        var changed = await browser.PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Metalworker",
            description = "We build railings and stairs.",
            location = "Berlin",
            remote_mode = "onsite",
            employment_type = "full_time",
            skills = new[] { "MIG/MAG", "WIG" }
        });

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// What is asked for is the company from the TOKEN and the human being from
    /// the <c>sub</c> — not anything from the request.
    /// </summary>
    /// <remarks>
    /// Without this test the fake could say "admin" while the caller passes a
    /// foreign company, and everything would look green. The tenant in the token
    /// never came from input: <c>POST /auth/company/{id}</c> issues it after the
    /// server has checked the membership (ADR-0018).
    /// </remarks>
    [Fact]
    public async Task The_tenant_from_the_token_is_what_gets_asked()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var browser = AsCompany(company, subject);
        var id = await Draft(browser);

        await browser.PostAsync($"/jobs/{id}/publish", null);

        _roles.Last.Should().Be((subject, company));
    }

    /// <summary>
    /// A silent role lookup is not a refusal.
    /// </summary>
    /// <remarks>
    /// 503 means "did not answer", 403 means "you may not" — the same
    /// distinction this tree makes at the consent ledger. Answering an outage of
    /// identity-service with 403 would be a lie that looks like a decision: the
    /// person in front of it reads "your permission was taken away", and nobody
    /// looks for an outage.
    /// </remarks>
    [Fact]
    public async Task A_silent_role_lookup_is_no_refusal()
    {
        var browser = AsCompany(Guid.CreateVersion7());
        var id = await Draft(browser);

        _roles.Silent = true;
        var response = await browser.PostAsync($"/jobs/{id}/publish", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var problem = await Json(response);
        problem.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Without a token 401, as a person without a company 403 — the policy
    /// changes nothing about that.
    /// </summary>
    /// <remarks>
    /// The two columns of the route map that existed before PBI-2. They stand
    /// here because a new policy easily turns a 403 into a 401: the framework
    /// asks for sign-in first and only then for the permission.
    /// </remarks>
    [Fact]
    public async Task Without_token_and_without_company_it_stays_as_it_was()
    {
        var id = await Draft(AsCompany(Guid.CreateVersion7()));

        var anonymous = await _service.CreateClient().PostAsync($"/jobs/{id}/publish", null);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = _service.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Person(Guid.CreateVersion7()));

        (await person.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
