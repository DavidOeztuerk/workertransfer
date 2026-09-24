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
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly RoleProbe _roles = new();

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Geheimnis", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, ProbeDelivery>());
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

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private HttpClient AsCompany(Guid company, Guid? subject = null) =>
        With(TokenShape.Company(subject ?? Guid.CreateVersion7(), company));

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A running case, up to just before the offer.</summary>
    private async Task<(Guid Case, Guid Subject)> InConversation(Guid company)
    {
        var subject = Guid.CreateVersion7();

        await AsPerson(subject).PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        _ledger.MarketReleased.Add((subject, company));

        var created = await AsCompany(company).PostAsJsonAsync("/transfers", new
        {
            subject_id = subject, message = "We might have something for you."
        });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await Json(created)).GetProperty("id").GetGuid();

        (await AsPerson(subject).PostAsync($"/transfers/{id}/accept-talk", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return (id, subject);
    }

    private static Task<HttpResponseMessage> Offer(HttpClient browser, Guid caseItem) =>
        browser.PostAsJsonAsync($"/transfers/{caseItem}/offer", new
        {
            note = "We are offering the job.", start_on = "2026-10", fee_cents = 250_000L
        });

    /// <summary>A <c>member</c> makes no offer.</summary>
    [Fact]
    public async Task Only_an_admin_makes_an_offer()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await InConversation(company);

        _roles.Answer = CompanyRole.Member;
        var asMember = await Offer(AsCompany(company), caseItem);

        asMember.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "an offer names a date and a fee");

        _roles.Answer = CompanyRole.Admin;
        var asAdmin = await Offer(AsCompany(company), caseItem);

        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>And completes no case.</summary>
    [Fact]
    public async Task Only_an_admin_completes_a_case()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, subject) = await InConversation(company);

        _roles.Answer = CompanyRole.Admin;
        (await Offer(AsCompany(company), caseItem)).StatusCode.Should().Be(HttpStatusCode.OK);

        // No `confirm-release`: the person here is not employed, so there is
        // nobody who would have to let them go.
        (await AsPerson(subject).PostAsync($"/transfers/{caseItem}/accept-offer", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _roles.Answer = CompanyRole.Member;
        var asMember = await AsCompany(company).PostAsync($"/transfers/{caseItem}/complete", null);

        asMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _roles.Answer = CompanyRole.Admin;
        var asAdmin = await AsCompany(company).PostAsync($"/transfers/{caseItem}/complete", null);

        asAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(asAdmin)).GetProperty("status").GetString().Should().Be("completed");
    }

    /// <summary>
    /// A <c>member</c> approaches, shows interest — and withdraws again.
    /// </summary>
    /// <remarks>
    /// The half that is easily lost. <c>withdraw</c> deliberately carries no
    /// company permission: a process one cannot get out of is no process but a
    /// trap — the same sentence <c>/decline</c> already carries for the
    /// person.
    /// </remarks>
    [Fact]
    public async Task A_member_shows_interest_and_withdraws_it()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await InConversation(company);

        _roles.Answer = CompanyRole.Member;

        var withdrawn = await AsCompany(company).PostAsync($"/transfers/{caseItem}/withdraw", null);

        withdrawn.StatusCode.Should().Be(
            HttpStatusCode.OK, "whoever may start must be allowed to stop");
    }

    /// <summary>What is asked for is the company from the TOKEN.</summary>
    [Fact]
    public async Task What_is_asked_for_is_the_tenant_from_the_token()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var (caseItem, _) = await InConversation(company);

        await Offer(AsCompany(company, subject), caseItem);

        _roles.Last.Should().Be((subject, company));
    }

    /// <summary>A silent role lookup is not a no.</summary>
    [Fact]
    public async Task A_silent_role_lookup_is_not_a_no()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, _) = await InConversation(company);

        _roles.Silent = true;
        var response = await Offer(AsCompany(company), caseItem);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Without a token 401, as a person without a company 403 — the policy
    /// changes nothing about that.
    /// </summary>
    [Fact]
    public async Task Without_a_token_and_without_a_company_it_stays_as_it_was()
    {
        var company = Guid.CreateVersion7();
        var (caseItem, subject) = await InConversation(company);

        (await Offer(_service.CreateClient(), caseItem))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Offer(AsPerson(subject), caseItem))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
