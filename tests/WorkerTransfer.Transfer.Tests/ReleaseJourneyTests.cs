using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Application.Ports;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>The way to the current employer: two consents, and a negotiation between companies (ADR-0050).</summary>
[Collection(PostgresCollection.Name)]
public class ReleaseJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();
    private readonly ProbeIdentity _identity = new();
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
            host.UseSetting("Erasure:Secret", "erasure-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IIdentityLookup>(_ => _identity));
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

    private HttpClient AsCompany(Guid company, Guid? member = null) =>
        With(TokenShape.Company(member ?? Guid.CreateVersion7(), company));

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>A running transfer between the hiring company and an employed person.</summary>
    private async Task<(Guid Case, Guid Anna)> Transfer(Guid hiring, bool employed = true)
    {
        var anna = Guid.CreateVersion7();
        _identity.Names[anna] = "Anna Schmidt";

        (await AsPerson(anna).PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed, note = string.Empty
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        _ledger.MarketReleased.Add((anna, hiring));

        var created = await AsCompany(hiring).PostAsJsonAsync("/transfers", new
        {
            subject_id = anna, message = "We might have something for you."
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return ((await Json(created)).GetProperty("id").GetGuid(), anna);
    }

    /// <summary>Both consents: she names her employer, the employer allows inquiries.</summary>
    private async Task Route(Guid anna, Guid employer, bool employerAllows = true)
    {
        _identity.Companies.Add(employer);

        (await AsPerson(anna).PutAsJsonAsync("/market/me/employer", new { employer_id = employer }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        if (employerAllows)
        {
            (await AsCompany(employer).PutAsJsonAsync("/transfers/employer/setting", new { accepts_inquiries = true }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private Task<HttpResponseMessage> Ask(Guid hiring, Guid caseId, string month = "2026-12", long? fee = 500_000) =>
        AsCompany(hiring).PostAsJsonAsync($"/transfers/{caseId}/release", new { release_month = month, fee_cents = fee });

    private static async Task<string> Stripped(HttpResponseMessage response)
    {
        var body = await Json(response);
        return string.Join("|", body.EnumerateObject()
            .Where(property => property.Name != "correlationId")
            .Select(property => $"{property.Name}={property.Value}"));
    }

    /// <summary>
    /// Without BOTH consents the route does not exist — and every way of it
    /// not existing answers the same.
    /// </summary>
    /// <remarks>
    /// Told apart, each 404 would tell the hiring company something: that she
    /// named an employer, that the employer is not asking, that she took it
    /// back.
    /// </remarks>
    [Fact]
    public async Task Without_both_consents_there_is_no_route_and_every_no_reads_the_same()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();

        var (nothingNamed, _) = await Transfer(hiring);
        var noRoute = await AsCompany(hiring).GetAsync($"/transfers/{nothingNamed}/release");

        var (employerSilent, bert) = await Transfer(hiring);
        await Route(bert, employer, employerAllows: false);
        var notAllowed = await AsCompany(hiring).GetAsync($"/transfers/{employerSilent}/release");

        var (revoked, clara) = await Transfer(hiring);
        await Route(clara, employer);
        _ledger.RoutesOpen.Remove((clara, employer));
        var takenBack = await AsCompany(hiring).GetAsync($"/transfers/{revoked}/release");

        var (open, dora) = await Transfer(hiring);
        await Route(dora, employer);
        var possible = await AsCompany(hiring).GetAsync($"/transfers/{open}/release");

        foreach (var refusal in new[] { noRoute, notAllowed, takenBack })
        {
            refusal.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await Stripped(refusal)).Should().Be(await Stripped(noRoute));
        }

        possible.StatusCode.Should().Be(HttpStatusCode.OK, await possible.Content.ReadAsStringAsync());
        var view = await Json(possible);
        view.GetProperty("status").GetString().Should().Be("possible");
        view.GetProperty("employer_id").GetGuid().Should().Be(employer);
        view.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Naming the employer and consenting are one act; closing takes both back.</summary>
    [Fact]
    public async Task Naming_the_employer_is_the_consent_and_closing_removes_both()
    {
        var anna = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        _identity.Companies.Add(employer);
        var asAnna = AsPerson(anna);

        var none = await Json(await asAnna.GetAsync("/market/me/employer"));
        none.GetProperty("employer_id").ValueKind.Should().Be(JsonValueKind.Null);

        (await asAnna.PutAsJsonAsync("/market/me/employer", new { employer_id = employer }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _ledger.RoutesOpen.Should().Contain((anna, employer), "naming IS the consent, written to the ledger");
        var open = await Json(await asAnna.GetAsync("/market/me/employer"));
        open.GetProperty("employer_id").GetGuid().Should().Be(employer);
        open.GetProperty("open").GetBoolean().Should().BeTrue();

        (await asAnna.DeleteAsync("/market/me/employer")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        _ledger.RoutesOpen.Should().NotContain((anna, employer));
        await using var context = postgres.Context();
        (await context.EmployerRoutes.AnyAsync(row => row.Id == anna))
            .Should().BeFalse("without the consent 'works at X' must not exist");
    }

    /// <summary>A revocation on /consents closes the route while the row still stands.</summary>
    [Fact]
    public async Task A_revocation_in_the_ledger_reads_as_a_closed_route()
    {
        var anna = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        _identity.Companies.Add(employer);

        await AsPerson(anna).PutAsJsonAsync("/market/me/employer", new { employer_id = employer });
        _ledger.RoutesOpen.Remove((anna, employer));

        var read = await Json(await AsPerson(anna).GetAsync("/market/me/employer"));
        read.GetProperty("open").GetBoolean().Should().BeFalse();
    }

    /// <summary>An unknown company is refused, and nothing is written anywhere.</summary>
    [Fact]
    public async Task An_unknown_employer_is_refused_without_a_trace()
    {
        var anna = Guid.CreateVersion7();
        var nowhere = Guid.CreateVersion7();

        var response = await AsPerson(anna).PutAsJsonAsync("/market/me/employer", new { employer_id = nowhere });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _ledger.RoutesOpen.Should().NotContain((anna, nowhere));
        await using var context = postgres.Context();
        (await context.EmployerRoutes.AnyAsync(row => row.Id == anna)).Should().BeFalse();
    }

    /// <summary>Another employer named: the first consent ends, it does not move.</summary>
    [Fact]
    public async Task Naming_another_employer_ends_the_first_route()
    {
        var anna = Guid.CreateVersion7();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        _identity.Companies.Add(first);
        _identity.Companies.Add(second);

        await AsPerson(anna).PutAsJsonAsync("/market/me/employer", new { employer_id = first });
        await AsPerson(anna).PutAsJsonAsync("/market/me/employer", new { employer_id = second });

        _ledger.RoutesOpen.Should().NotContain((anna, first));
        _ledger.RoutesOpen.Should().Contain((anna, second));
    }

    /// <summary>
    /// Asked, countered, accepted by the other side, agreed by her: settled.
    /// Both companies see her regular earliest start; only her employer sees
    /// her name.
    /// </summary>
    [Fact]
    public async Task A_negotiation_runs_to_settled_and_she_decides_last()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        await AsPerson(anna).PutAsJsonAsync("/market/me/notice", new { length = 3, unit = "month", end = "quarter_end" });

        var asked = await Ask(hiring, caseId, month: "2026-11", fee: 500_000);
        asked.StatusCode.Should().Be(HttpStatusCode.Created, await asked.Content.ReadAsStringAsync());
        var hiringView = await Json(asked);
        hiringView.GetProperty("status").GetString().Should().Be("asked");
        hiringView.GetProperty("terms_by").GetString().Should().Be("hiring");
        hiringView.GetProperty("regular_from").ValueKind.Should().Be(JsonValueKind.String);
        hiringView.TryGetProperty("person_name", out _).Should().BeFalse("the hiring company knows her already");
        var id = hiringView.GetProperty("id").GetGuid();

        var inquiries = (await Json(await AsCompany(employer).GetAsync("/transfers/employer/inquiries")))
            .EnumerateArray().ToList();
        inquiries.Should().ContainSingle();
        inquiries[0].GetProperty("person_name").GetString().Should().Be("Anna Schmidt");
        inquiries[0].GetProperty("side").GetString().Should().Be("current");
        inquiries[0].GetProperty("regular_from").ValueKind.Should().Be(JsonValueKind.String);

        var countered = await AsCompany(employer).PostAsJsonAsync(
            $"/transfers/releases/{id}/terms", new { release_month = "2027-01", fee_cents = 750_000L });
        countered.StatusCode.Should().Be(HttpStatusCode.OK, await countered.Content.ReadAsStringAsync());
        (await Json(countered)).GetProperty("terms_by").GetString().Should().Be("current");

        (await AsCompany(hiring).PostAsync($"/transfers/releases/{id}/accept", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var hers = await Json(await AsPerson(anna).GetAsync($"/transfers/{caseId}/release"));
        hers.GetProperty("status").GetString().Should().Be("agreed");
        hers.GetProperty("fee_cents").GetInt64().Should().Be(750_000);
        hers.GetProperty("side").GetString().Should().Be("person");

        var settled = await AsPerson(anna).PostAsync($"/transfers/releases/{id}/agree", null);
        settled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(settled)).GetProperty("status").GetString().Should().Be("settled");

        _identity.NamesAsked.Should().OnlyContain(subject => subject == anna);
    }

    /// <summary>Whoever set the terms cannot accept them in the same breath.</summary>
    [Fact]
    public async Task The_side_that_set_the_terms_cannot_accept_them()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();

        (await AsCompany(hiring).PostAsync($"/transfers/releases/{id}/accept", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AsCompany(employer).PostAsync($"/transfers/releases/{id}/accept", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>She can say no in every running state — also after both companies agreed.</summary>
    [Fact]
    public async Task She_can_decline_after_the_companies_agreed()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();
        await AsCompany(employer).PostAsync($"/transfers/releases/{id}/accept", null);

        var declined = await AsPerson(anna).PostAsync($"/transfers/releases/{id}/decline", null);

        declined.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(declined)).GetProperty("status").GetString().Should().Be("declined");
    }

    /// <summary>
    /// A revocation stops both companies at the next call; she goes on reading
    /// what was negotiated about her.
    /// </summary>
    [Fact]
    public async Task A_revoked_route_hides_the_negotiation_from_both_companies_and_not_from_her()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();

        _ledger.RoutesOpen.Remove((anna, employer));

        (await AsCompany(hiring).GetAsync($"/transfers/{caseId}/release"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Json(await AsCompany(employer).GetAsync("/transfers/employer/inquiries")))
            .GetArrayLength().Should().Be(0);
        (await AsCompany(employer).PostAsync($"/transfers/releases/{id}/refuse", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AsPerson(anna).GetAsync($"/transfers/{caseId}/release"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>The ledger is asked at every read — a cache in front of it would show here.</summary>
    [Fact]
    public async Task The_ledger_is_asked_at_every_read()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        await Ask(hiring, caseId);

        var before = _ledger.RouteQuestions;
        await AsCompany(hiring).GetAsync($"/transfers/{caseId}/release");
        await AsCompany(hiring).GetAsync($"/transfers/{caseId}/release");

        _ledger.RouteQuestions.Should().Be(before + 2);
    }

    /// <summary>Closing the route ends what runs over it.</summary>
    [Fact]
    public async Task Closing_the_route_ends_a_running_negotiation()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        await Ask(hiring, caseId);

        await AsPerson(anna).DeleteAsync("/market/me/employer");

        var hers = await Json(await AsPerson(anna).GetAsync($"/transfers/{caseId}/release"));
        hers.GetProperty("status").GetString().Should().Be("closed");
    }

    /// <summary>A transfer that ends takes its running negotiation with it.</summary>
    [Fact]
    public async Task Ending_the_transfer_closes_the_negotiation()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        await Ask(hiring, caseId);

        (await AsPerson(anna).PostAsync($"/transfers/{caseId}/decline", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var hers = await Json(await AsPerson(anna).GetAsync($"/transfers/{caseId}/release"));
        hers.GetProperty("status").GetString().Should().Be("closed");
    }

    /// <summary>Somebody not employed has nobody to be released by.</summary>
    [Fact]
    public async Task A_transfer_without_a_release_has_no_route()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring, employed: false);
        await Route(anna, employer);

        (await AsCompany(hiring).GetAsync($"/transfers/{caseId}/release"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Ask(hiring, caseId)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>The hiring company cannot ask itself: if it is her employer, there is no route.</summary>
    [Fact]
    public async Task The_own_employer_is_no_route()
    {
        var hiring = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, hiring);

        (await AsCompany(hiring).GetAsync($"/transfers/{caseId}/release"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>A month is a month, and a fee is not negative.</summary>
    [Theory]
    [InlineData("Dezember", 500_000L)]
    [InlineData("2026-12", -1L)]
    public async Task Bad_terms_are_refused(string month, long fee)
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);

        (await Ask(hiring, caseId, month, fee)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>One negotiation per transfer: a refusal stands.</summary>
    [Fact]
    public async Task A_second_question_is_refused()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();
        await AsCompany(employer).PostAsync($"/transfers/releases/{id}/refuse", null);

        (await Ask(hiring, caseId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Steps bind a company: asking, terms and accepting need an admin; saying no needs nobody.</summary>
    [Fact]
    public async Task Binding_steps_need_an_admin_and_saying_no_does_not()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();

        _roles.Answer = CompanyRole.Recruiter;

        (await AsCompany(employer).PostAsync($"/transfers/releases/{id}/accept", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AsCompany(employer).PutAsJsonAsync("/transfers/employer/setting", new { accepts_inquiries = false }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AsCompany(employer).PostAsync($"/transfers/releases/{id}/refuse", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>A step on the other party's side is a conflict, never a step.</summary>
    [Fact]
    public async Task Each_step_belongs_to_one_party()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();

        (await AsCompany(hiring).PostAsync($"/transfers/releases/{id}/refuse", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AsCompany(employer).PostAsync($"/transfers/releases/{id}/withdraw", null))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await AsCompany(Guid.CreateVersion7()).PostAsync($"/transfers/releases/{id}/refuse", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "a third company does not learn it exists");
        (await AsPerson(Guid.CreateVersion7()).PostAsync($"/transfers/releases/{id}/decline", null))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Company steps reach her and the other company's members — a kind, no
    /// name; her own steps reach nobody.
    /// </summary>
    [Fact]
    public async Task Company_steps_notify_her_and_the_other_side()
    {
        var hiring = Guid.CreateVersion7();
        var employer = Guid.CreateVersion7();
        var hiringMember = Guid.CreateVersion7();
        var employerMember = Guid.CreateVersion7();
        var (caseId, anna) = await Transfer(hiring);
        await Route(anna, employer);
        _identity.Members[employer] = [employerMember, anna];
        _identity.Members[hiring] = [hiringMember];

        var id = (await Json(await Ask(hiring, caseId))).GetProperty("id").GetGuid();
        await AsCompany(employer).PostAsync($"/transfers/releases/{id}/accept", null);
        var before = await Notes();
        await AsPerson(anna).PostAsync($"/transfers/releases/{id}/agree", null);

        var notes = await Notes();
        notes.Should().Contain((employerMember, "transfer_update"));
        notes.Should().Contain((hiringMember, "transfer_update"));
        notes.Count(note => note.User == anna && note.Kind == "transfer_update")
            .Should().BeGreaterThanOrEqualTo(2, "the case and both company steps reached her");
        notes.Count.Should().Be(before.Count, "her own step notifies nobody");

        async Task<List<(Guid User, string Kind)>> Notes()
        {
            await using var context = postgres.Context();
            return (await context.Set<OutboxRow>()
                    .Where(row => row.UserId == anna || row.UserId == employerMember || row.UserId == hiringMember)
                    .Select(row => new { row.UserId, row.Kind })
                    .ToListAsync())
                .Select(row => (row.UserId, row.Kind))
                .ToList();
        }
    }

    /// <summary>A silent identity-service is 503 — never "no such company".</summary>
    [Fact]
    public async Task A_silent_identity_service_is_503()
    {
        _identity.Silent = true;

        var response = await AsPerson(Guid.CreateVersion7())
            .PutAsJsonAsync("/market/me/employer", new { employer_id = Guid.CreateVersion7() });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>The setting reads "no" until somebody switches it, and a person has none.</summary>
    [Fact]
    public async Task The_setting_starts_at_no_and_belongs_to_a_company()
    {
        var company = Guid.CreateVersion7();

        var read = await Json(await AsCompany(company).GetAsync("/transfers/employer/setting"));
        read.GetProperty("accepts_inquiries").GetBoolean().Should().BeFalse();

        (await AsPerson(Guid.CreateVersion7()).GetAsync("/transfers/employer/setting"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
