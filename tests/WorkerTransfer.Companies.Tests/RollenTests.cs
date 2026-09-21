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
/// Das Schaufenster schreibt, wer für das Unternehmen sprechen darf.
/// </summary>
/// <remarks>
/// Ein EmployerProfile trägt Name, Beschreibung, PostalAddress und das Kürzel,
/// unter dem die Karriereseite <strong>ohne Anmeldung</strong> erreichbar ist.
/// Wer es ändert, ändert, wie das Unternehmen in der Welt erscheint — und zwar
/// für alle anderen mit.
/// <para>
/// Lesen bleibt jedem Mitglied: es steht ohnehin im Netz.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class RollenTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Rollenprobe _rollen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:companies", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _rollen)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid company, Guid? subject = null)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(subject ?? Guid.CreateVersion7(), company));
        return browser;
    }

    private static Task<HttpResponseMessage> Write(HttpClient browser, string name) =>
        browser.PutAsJsonAsync("/companies/me/profile", new
        {
            display_name = name,
            about = "Wir bauen Gelaender.",
            website = "https://muster.example",
            locations = new[] { "Berlin" },
            benefits = new[] { "Werkzeug gestellt" }
        });

    /// <summary>Ein <c>member</c> schreibt das Schaufenster nicht.</summary>
    [Fact]
    public async Task Only_an_admin_writes_the_employer_profile()
    {
        var browser = AlsFirma(Guid.CreateVersion7());

        _rollen.Antwort = CompanyRole.Mitglied;
        var alsMitglied = await Write(browser, "Muster GmbH");

        alsMitglied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Die Gegenprobe in derselben Reihe: ein Endpunkt, der immer 403 gibt,
        // saehe von hier aus genauso aus.
        _rollen.Antwort = CompanyRole.Admin;
        var alsAdmin = await Write(browser, "Muster GmbH");

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Lesen darf jedes Mitglied — sonst waere die Einladung eine
    /// Zuschauerkarte.
    /// </summary>
    [Fact]
    public async Task A_member_reads_their_own_shop_window()
    {
        var company = Guid.CreateVersion7();

        _rollen.Antwort = CompanyRole.Admin;
        (await Write(AlsFirma(company), "Muster GmbH"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _rollen.Antwort = CompanyRole.Mitglied;
        var gelesen = await AlsFirma(company).GetAsync("/companies/me/profile");

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonDocument.Parse(await gelesen.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("display_name").GetString().Should().Be("Muster GmbH");
    }

    /// <summary>
    /// Gefragt wird nach der Firma aus dem TOKEN, nicht nach etwas aus der
    /// ResumeRequest.
    /// </summary>
    [Fact]
    public async Task The_tenant_asked_for_comes_from_the_token()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();

        await Write(AlsFirma(company, subject), "Muster GmbH");

        _rollen.Zuletzt.Should().Be((subject, company));
    }

    /// <summary>Schweigt die Rollenauskunft, ist das kein Nein.</summary>
    [Fact]
    public async Task A_silent_role_lookup_is_not_a_no()
    {
        _rollen.Schweigt = true;

        var response = await Write(AlsFirma(Guid.CreateVersion7()), "Muster GmbH");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Ohne Token 401, als Person ohne Firma 403 — die Richtlinie aendert
    /// daran nichts.
    /// </summary>
    [Fact]
    public async Task Without_a_token_and_without_a_company_it_stays_as_it_was()
    {
        var ohne = await Write(_dienst.CreateClient(), "Muster GmbH");
        ohne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = _dienst.CreateClient();
        person.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Person(Guid.CreateVersion7()));

        (await Write(person, "Muster GmbH")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
