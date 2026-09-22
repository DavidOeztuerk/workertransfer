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
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>
/// The line lies at the money, not at the conversation.
/// </summary>
/// <remarks>
/// <para>Showing interest, approaching somebody, reading one's own cases —
/// approach work, and with it the work somebody is invited for. An
/// <em>offer</em> names a start date and a placement fee, and a
/// <em>completion</em> states that both hold: both bind the company.</para>
///
/// <para><strong><c>withdraw</c> deliberately does not stand with them</strong>,
/// and the suite for it stands here: whoever may begin a case has to be allowed
/// to end it. Otherwise the invitation is a trap.</para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class CompanyRoleTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly RoleProbe _rollen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", "loesch-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, Probezustellung>());
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _rollen));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private HttpClient AlsFirma(Guid company, Guid? subject = null) =>
        Mit(TokenShape.Company(subject ?? Guid.CreateVersion7(), company));

    private HttpClient AlsPerson(Guid subject) => Mit(TokenShape.Person(subject));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Ein laufender Case, bis kurz vor das Angebot.</summary>
    private async Task<(Guid Case, Guid Subject)> ImGespraech(Guid company)
    {
        var subject = Guid.CreateVersion7();

        await AlsPerson(subject).PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        _ledger.MarktFrei.Add((subject, company));

        var angelegt = await AlsFirma(company).PostAsJsonAsync("/transfers", new
        {
            subject_id = subject, message = "Wir hätten da etwas."
        });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await AlsPerson(subject).PostAsync($"/transfers/{id}/accept-talk", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (id, subject);
    }

    private static Task<HttpResponseMessage> Angebot(HttpClient browser, Guid caseItem) =>
        browser.PostAsJsonAsync($"/transfers/{caseItem}/offer", new
        {
            note = "Wir bieten die Stelle an.", start_on = "2026-10", fee_cents = 250_000L
        });

    /// <summary>Ein <c>member</c> macht kein Angebot.</summary>
    [Fact]
    public async Task Only_an_admin_makes_an_offer()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await ImGespraech(company);

        _rollen.Antwort = CompanyRole.Mitglied;
        var alsMitglied = await Angebot(AlsFirma(company), caseItem);

        alsMitglied.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "ein Angebot nennt Termin und Gebuehr");

        _rollen.Antwort = CompanyRole.Admin;
        var alsAdmin = await Angebot(AlsFirma(company), caseItem);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Und schliesst keinen Case ab.</summary>
    [Fact]
    public async Task Only_an_admin_completes_a_case()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, subject) = await ImGespraech(company);

        _rollen.Antwort = CompanyRole.Admin;
        (await Angebot(AlsFirma(company), caseItem)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Kein `confirm-release`: die Person hier ist nicht beschaeftigt, es
        // gibt also niemanden, der sie gehen lassen muesste.
        (await AlsPerson(subject).PostAsync($"/transfers/{caseItem}/accept-offer", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _rollen.Antwort = CompanyRole.Mitglied;
        var alsMitglied = await AlsFirma(company).PostAsync($"/transfers/{caseItem}/complete", null);

        alsMitglied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _rollen.Antwort = CompanyRole.Admin;
        var alsAdmin = await AlsFirma(company).PostAsync($"/transfers/{caseItem}/complete", null);

        alsAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(alsAdmin)).GetProperty("status").GetString().Should().Be("completed");
    }

    /// <summary>
    /// Ein <c>member</c> spricht an, zeigt Interesse — und zieht wieder
    /// zurueck.
    /// </summary>
    /// <remarks>
    /// Die Haelfte, die leicht verlorengeht. <c>withdraw</c> traegt absichtlich
    /// kein Firmenrecht: ein Verfahren, aus dem man nicht aussteigen kann, ist
    /// kein Verfahren, sondern eine Falle — derselbe Satz, den <c>/decline</c>
    /// fuer die Person schon traegt.
    /// </remarks>
    [Fact]
    public async Task A_member_shows_interest_and_withdraws_it()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await ImGespraech(company);

        _rollen.Antwort = CompanyRole.Mitglied;

        var zurueck = await AlsFirma(company).PostAsync($"/transfers/{caseItem}/withdraw", null);

        zurueck.StatusCode.Should().Be(
            HttpStatusCode.OK, "wer anfangen darf, muss aufhoeren duerfen");
    }

    /// <summary>Gefragt wird nach der Firma aus dem TOKEN.</summary>
    [Fact]
    public async Task What_is_asked_for_is_the_tenant_from_the_token()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var (caseItem, _) = await ImGespraech(company);

        await Angebot(AlsFirma(company, subject), caseItem);

        _rollen.Zuletzt.Should().Be((subject, company));
    }

    /// <summary>Schweigt die Rollenauskunft, ist das kein Nein.</summary>
    [Fact]
    public async Task A_silent_role_lookup_is_not_a_no()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await ImGespraech(company);

        _rollen.Schweigt = true;
        var response = await Angebot(AlsFirma(company), caseItem);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Ohne Token 401, als Person ohne Firma 403 — die Richtlinie aendert
    /// daran nichts.
    /// </summary>
    [Fact]
    public async Task Without_a_token_and_without_a_company_it_stays_as_it_was()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, subject) = await ImGespraech(company);

        (await Angebot(_dienst.CreateClient(), caseItem))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Angebot(AlsPerson(subject), caseItem))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
