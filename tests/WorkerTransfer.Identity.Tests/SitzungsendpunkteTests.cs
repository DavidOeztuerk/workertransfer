using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// <c>GET /auth/session</c>, <c>GET /me</c> and <c>POST /auth/company/{id}</c>,
/// over HTTP against the real schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SessionEndpointTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "geheim-und-lang-genug";

    /// <summary>Written by <c>worker_auth.BcryptPasswordHasher</c>, verbatim.</summary>
    private const string PythonEntry =
        "$2b$12$DK/g90pKN70aYUJosVXXue7nxXxqQoBDevGWH/2zQSzfzqNLjn9eW";

    private WebApplicationFactory<Program> _dienst = null!;
    private string _email = null!;
    private Guid _anna;

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("environment", "Development");
        });

        using (var scope = _dienst.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync();
        }

        _anna = Guid.NewGuid();
        _email = $"anna-{Guid.NewGuid():N}@example.com";
        await CreateAccount(_anna, _email);
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private async Task CreateAccount(Guid id, string email)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, 'Anna', 'active'::account_status, '["user"]'::jsonb,
                    now(), now(), 1)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", PythonEntry);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Granted out of band, the same seam the Python integration tests use: this
    /// suite is about the endpoints, not about how somebody joins a company.
    /// </summary>
    private async Task<Guid> CreateCompanyWithMember(Guid users)
    {
        var company = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO tenants (id, name, domain, status, created_at)
                VALUES (@id, 'Beispiel GmbH', @domain, 'active', now())
                """;
            command.Parameters.AddWithValue("id", company);
            command.Parameters.AddWithValue("domain", $"{company:N}.example.com");
            await command.ExecuteNonQueryAsync();
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO user_tenant_memberships (id, user_id, tenant_id, role, granted_at)
                VALUES (@id, @benutzer, @company, 'admin', now())
                """;
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("benutzer", users);
            command.Parameters.AddWithValue("company", company);
            await command.ExecuteNonQueryAsync();
        }

        return company;
    }

    /// <summary>Somebody else, so the foreign membership has a real owner.</summary>
    private async Task<Guid> ForeignAccount()
    {
        var subject = Guid.NewGuid();
        await CreateAccount(subject, $"bea-{Guid.NewGuid():N}@example.com");
        return subject;
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> SignedInBrowser()
    {
        var browser = Browser();
        var response = await browser.PostAsJsonAsync(
            "/auth/login", new { email = _email, password = Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return browser;
    }

    private static async Task<Dictionary<string, object?>> Json(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Dictionary<string, object?>>())!;

    /// <summary>
    /// Public, and always 200. Asking this through <c>/me</c> would produce a
    /// 401 for every signed-out visitor — a failure entry about a completely
    /// normal state.
    /// </summary>
    [Fact]
    public async Task With_nothing_at_all_the_session_reports_anonymous_and_not_401()
    {
        var response = await Browser().GetAsync("/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["state"]!.ToString().Should().Be("anonymous");
    }

    [Fact]
    public async Task Signed_in_the_session_reports_active_with_the_address()
    {
        var response = await (await SignedInBrowser()).GetAsync("/auth/session");

        var köbody = await Json(response);
        köbody["state"]!.ToString().Should().Be("active");
        köbody["user"]!.ToString().Should().Contain(_email);
    }

    [Fact]
    public async Task Without_a_sign_in_me_answers_401()
    {
        var response = await Browser().GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_me_names_the_person_and_no_company()
    {
        var response = await (await SignedInBrowser()).GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var köbody = await Json(response);
        köbody["user_id"]!.ToString().Should().Be(_anna.ToString());
        köbody["email"]!.ToString().Should().Be(_email);
        köbody["tenant_id"].Should().BeNull("a sign-in makes nobody a company");
    }

    [Fact]
    public async Task A_member_may_act_for_their_company()
    {
        var company = await CreateCompanyWithMember(_anna);
        var browser = await SignedInBrowser();

        var switched = await browser.PostAsync($"/auth/company/{company}", null);

        switched.StatusCode.Should().Be(HttpStatusCode.OK);

        var köbody = await Json(await browser.GetAsync("/me"));
        köbody["tenant_id"]!.ToString().Should().Be(company.ToString());
    }

    /// <summary>
    /// 403 and not 404: a different answer for "no such company" would let
    /// anyone probe which companies are here.
    /// </summary>
    [Fact]
    public async Task Whoever_is_not_a_member_gets_403_whether_or_not_the_company_exists()
    {
        var foreign = await CreateCompanyWithMember(await ForeignAccount());
        var invented = Guid.NewGuid();
        var browser = await SignedInBrowser();

        var withForeign = await browser.PostAsync($"/auth/company/{foreign}", null);
        var withInvented = await browser.PostAsync($"/auth/company/{invented}", null);

        withForeign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        withInvented.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_sign_in_nobody_switches_company()
    {
        var company = await CreateCompanyWithMember(_anna);

        var response = await Browser().PostAsync($"/auth/company/{company}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Fails on the refusal too, and that is the half worth keeping: it names
    /// the company somebody asked for and was not given.
    /// </summary>
    [Fact]
    public async Task The_switch_and_its_refusal_both_stand_in_the_trail()
    {
        var mine = await CreateCompanyWithMember(_anna);
        var foreign = await CreateCompanyWithMember(await ForeignAccount());
        var browser = await SignedInBrowser();

        await browser.PostAsync($"/auth/company/{mine}", null);
        await browser.PostAsync($"/auth/company/{foreign}", null);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT action::text, tenant_id FROM audit_events
            WHERE actor_id = @subject AND action IN ('tenant_switch', 'tenant_switch_denied')
            ORDER BY occurred_at
            """;
        command.Parameters.AddWithValue("subject", _anna);

        var found = new List<(string Action, Guid Company)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add((reader.GetString(0), reader.GetGuid(1)));
        }

        found.Should().Contain(("tenant_switch", mine));
        found.Should().Contain(("tenant_switch_denied", foreign));
    }
}
