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
public class SitzungsendpunkteTests(Postgres postgres) : IAsyncLifetime
{
    private const string Passwort = "geheim-und-lang-genug";

    /// <summary>Written by <c>worker_auth.BcryptPasswordHasher</c>, verbatim.</summary>
    private const string PythonEintrag =
        "$2b$12$DK/g90pKN70aYUJosVXXue7nxXxqQoBDevGWH/2zQSzfzqNLjn9eW";

    private WebApplicationFactory<Program> _dienst = null!;
    private string _email = null!;
    private Guid _anna;

    public async Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:PrivateKey", Tokenform.PrivatSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
        });

        using (var scope = _dienst.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
                .Database.MigrateAsync();
        }

        _anna = Guid.NewGuid();
        _email = $"anna-{Guid.NewGuid():N}@example.com";
        await LegeKontoAn(_anna, _email);
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private async Task LegeKontoAn(Guid id, string email)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, 'Anna', 'active'::account_status, '["user"]'::jsonb,
                    now(), now(), 1)
            """;
        befehl.Parameters.AddWithValue("id", id);
        befehl.Parameters.AddWithValue("email", email);
        befehl.Parameters.AddWithValue("hash", PythonEintrag);
        await befehl.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Granted out of band, the same seam the Python integration tests use: this
    /// suite is about the endpoints, not about how somebody joins a company.
    /// </summary>
    private async Task<Guid> LegeFirmaAnMitMitglied(Guid benutzer)
    {
        var company = Guid.NewGuid();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using (var befehl = connection.CreateCommand())
        {
            befehl.CommandText = """
                INSERT INTO tenants (id, name, domain, status, created_at)
                VALUES (@id, 'Beispiel GmbH', @domain, 'active', now())
                """;
            befehl.Parameters.AddWithValue("id", company);
            befehl.Parameters.AddWithValue("domain", $"{company:N}.example.com");
            await befehl.ExecuteNonQueryAsync();
        }

        await using (var befehl = connection.CreateCommand())
        {
            befehl.CommandText = """
                INSERT INTO user_tenant_memberships (id, user_id, tenant_id, role, granted_at)
                VALUES (@id, @benutzer, @company, 'admin', now())
                """;
            befehl.Parameters.AddWithValue("id", Guid.NewGuid());
            befehl.Parameters.AddWithValue("benutzer", benutzer);
            befehl.Parameters.AddWithValue("company", company);
            await befehl.ExecuteNonQueryAsync();
        }

        return company;
    }

    /// <summary>Somebody else, so the foreign membership has a real owner.</summary>
    private async Task<Guid> FremdesKonto()
    {
        var subject = Guid.NewGuid();
        await LegeKontoAn(subject, $"bea-{Guid.NewGuid():N}@example.com");
        return subject;
    }

    private HttpClient Browser() =>
        _dienst.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<HttpClient> AngemeldeterBrowser()
    {
        var browser = Browser();
        var response = await browser.PostAsJsonAsync(
            "/auth/login", new { email = _email, password = Passwort });
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
    public async Task Ohne_alles_meldet_die_Sitzung_anonymous_und_nicht_401()
    {
        var response = await Browser().GetAsync("/auth/session");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response))["state"]!.ToString().Should().Be("anonymous");
    }

    [Fact]
    public async Task Angemeldet_meldet_die_Sitzung_active_mit_der_Adresse()
    {
        var response = await (await AngemeldeterBrowser()).GetAsync("/auth/session");

        var körper = await Json(response);
        körper["state"]!.ToString().Should().Be("active");
        körper["user"]!.ToString().Should().Contain(_email);
    }

    [Fact]
    public async Task Ohne_Anmeldung_antwortet_me_mit_401()
    {
        var response = await Browser().GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Angemeldet_nennt_me_die_Person_und_keine_Firma()
    {
        var response = await (await AngemeldeterBrowser()).GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var körper = await Json(response);
        körper["user_id"]!.ToString().Should().Be(_anna.ToString());
        körper["email"]!.ToString().Should().Be(_email);
        körper["tenant_id"].Should().BeNull("eine Anmeldung macht niemanden zum Unternehmen");
    }

    [Fact]
    public async Task Ein_Mitglied_darf_fuer_seine_Firma_handeln()
    {
        var company = await LegeFirmaAnMitMitglied(_anna);
        var browser = await AngemeldeterBrowser();

        var gewechselt = await browser.PostAsync($"/auth/company/{company}", null);

        gewechselt.StatusCode.Should().Be(HttpStatusCode.OK);

        var körper = await Json(await browser.GetAsync("/me"));
        körper["tenant_id"]!.ToString().Should().Be(company.ToString());
    }

    /// <summary>
    /// 403 and not 404: a different answer for "no such company" would let
    /// anyone probe which companies are here.
    /// </summary>
    [Fact]
    public async Task Wer_kein_Mitglied_ist_bekommt_403_egal_ob_es_die_Firma_gibt()
    {
        var fremde = await LegeFirmaAnMitMitglied(await FremdesKonto());
        var erfunden = Guid.NewGuid();
        var browser = await AngemeldeterBrowser();

        var beiFremder = await browser.PostAsync($"/auth/company/{fremde}", null);
        var beiErfundener = await browser.PostAsync($"/auth/company/{erfunden}", null);

        beiFremder.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        beiErfundener.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ohne_Anmeldung_wechselt_niemand_die_Firma()
    {
        var company = await LegeFirmaAnMitMitglied(_anna);

        var response = await Browser().PostAsync($"/auth/company/{company}", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Fails on the refusal too, and that is the half worth keeping: it names
    /// the company somebody asked for and was not given.
    /// </summary>
    [Fact]
    public async Task Der_Wechsel_und_seine_Abweisung_stehen_beide_im_Protokoll()
    {
        var meine = await LegeFirmaAnMitMitglied(_anna);
        var fremde = await LegeFirmaAnMitMitglied(await FremdesKonto());
        var browser = await AngemeldeterBrowser();

        await browser.PostAsync($"/auth/company/{meine}", null);
        await browser.PostAsync($"/auth/company/{fremde}", null);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = """
            SELECT action::text, tenant_id FROM audit_events
            WHERE actor_id = @subject AND action IN ('tenant_switch', 'tenant_switch_denied')
            ORDER BY occurred_at
            """;
        befehl.Parameters.AddWithValue("subject", _anna);

        var found = new List<(string Handlung, Guid Firma)>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            found.Add((leser.GetString(0), leser.GetGuid(1)));
        }

        found.Should().Contain(("tenant_switch", meine));
        found.Should().Contain(("tenant_switch_denied", fremde));
    }
}
