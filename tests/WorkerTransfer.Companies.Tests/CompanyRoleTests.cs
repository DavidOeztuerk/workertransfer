using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Companies.Tests;

/// <summary>
/// The shop window is written by whoever may speak for the company.
/// </summary>
/// <remarks>
/// An employer profile carries name, description, postal address and the slug
/// under which the careers page is reachable <strong>without signing in</strong>.
/// Whoever changes it changes how the company appears in the world — and does
/// so for everybody else as well.
/// <para>
/// Reading stays open to every member: it is on the net anyway.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class CompanyRoleTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly RoleProbe _roles = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _roles)));
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

    private static Task<HttpResponseMessage> Write(HttpClient browser, string name) =>
        browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = name,
            about = "We build railings.",
            website = "https://example.test",
            locations = new[] { "Berlin" },
            benefits = new[] { "Tools provided" }
        });

    /// <summary>A <c>member</c> does not write the shop window.</summary>
    [Fact]
    public async Task Only_an_admin_writes_the_employer_profile()
    {
        var browser = AsCompany(Guid.CreateVersion7());

        _roles.Answer = CompanyRole.Mitglied;
        var asMember = await Write(browser, "Example GmbH");

        asMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The counter-probe in the same suite: an endpoint that always gives
        // 403 would look exactly the same from here.
        _roles.Answer = CompanyRole.Admin;
        var asAdmin = await Write(browser, "Example GmbH");

        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Every member may read — otherwise the invitation would be a spectator
    /// seat.
    /// </summary>
    [Fact]
    public async Task A_member_reads_their_own_shop_window()
    {
        var company = Guid.CreateVersion7();

        _roles.Answer = CompanyRole.Admin;
        (await Write(AsCompany(company), "Example GmbH"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _roles.Answer = CompanyRole.Mitglied;
        var read = await AsCompany(company).GetAsync("/companies/me/profile");

        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("display_name").GetString().Should().Be("Example GmbH");
    }

    /// <summary>
    /// What is asked for is the company out of the TOKEN, not something out of
    /// the request.
    /// </summary>
    [Fact]
    public async Task The_tenant_asked_for_comes_from_the_token()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();

        await Write(AsCompany(company, subject), "Example GmbH");

        _roles.Last.Should().Be((subject, company));
    }

    /// <summary>A silent role lookup is not a no.</summary>
    [Fact]
    public async Task A_silent_role_lookup_is_not_a_no()
    {
        _roles.Silent = true;

        var response = await Write(AsCompany(Guid.CreateVersion7()), "Example GmbH");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Without a token 401, as a person without a company 403 — the policy
    /// changes nothing about that.
    /// </summary>
    [Fact]
    public async Task Without_a_token_and_without_a_company_it_stays_as_it_was()
    {
        var without = await Write(_service.CreateClient(), "Example GmbH");
        without.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = _service.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Person(Guid.CreateVersion7()));

        (await Write(person, "Example GmbH")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
