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
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Transfer.Tests;

/// <summary>The market status, its release and its revocation.</summary>
[Collection(PostgresCollection.Name)]
public class MarketJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeLedger _ledger = new();

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
                // Offer and completion demand an `admin` since PBI-2. These
                // suites measure the case and not the role — so the probe
                // answers like an owner. `CompanyRoleTests` turns it to
                // `member`.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company) => With(TokenShape.Company(Guid.CreateVersion7(), company));

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient With(string token)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>The problem body without the correlation id.</summary>
    private static string WithoutCorrelation(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body, "\"correlationId\":\"[^\"]*\",?", string.Empty);

    private static Task<HttpResponseMessage> Set(
        HttpClient browser, string state, bool isEmployed = false, string remark = "") =>
        browser.PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = isEmployed, note = remark
        });

    /// <summary>
    /// Whoever has said nothing has not said "I am listening".
    /// </summary>
    /// <remarks>
    /// The default must never fall in the market's favour — and it is never
    /// <c>null</c>: "said nothing" IS a state.
    /// </remarks>
    [Fact]
    public async Task Whoever_has_said_nothing_is_not_available()
    {
        var response = await AsPerson(Guid.CreateVersion7()).GetAsync("/market/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await Json(response);
        status.GetProperty("availability").GetString().Should().Be("unavailable");
        status.GetProperty("is_approachable").GetBoolean().Should().BeFalse();
    }

    /// <summary>Every transition is allowed — it is a statement about the will.</summary>
    [Fact]
    public async Task Every_state_can_be_changed_into_every_other()
    {
        var browser = AsPerson(Guid.CreateVersion7());

        foreach (var state in new[] { "listening", "unavailable", "open", "listening" })
        {
            var response = await Set(browser, state);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Json(response)).GetProperty("availability").GetString().Should().Be(state);
        }
    }

    /// <summary>A word that does not exist is 422 and no crash.</summary>
    [Fact]
    public async Task An_invented_state_is_422()
    {
        var response = await Set(AsPerson(Guid.CreateVersion7()), "maybe");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Without a release a company sees nothing — and "not present" looks
    /// exactly the same.
    /// </summary>
    /// <remarks>
    /// The difference would be especially expensive here: the mere existence of
    /// the statement gives something away.
    /// </remarks>
    [Fact]
    public async Task Hidden_and_non_existent_answer_the_same()
    {
        var anna = Guid.CreateVersion7();
        await Set(AsPerson(anna), "open");

        var company = AsCompany(Guid.CreateVersion7());

        var hidden = await company.GetAsync($"/market/{anna}");
        var nobody = await company.GetAsync($"/market/{Guid.CreateVersion7()}");

        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        nobody.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Byte-identical apart from the correlation id — that differs per
        // request and says nothing about the person.
        WithoutCorrelation(await hidden.Content.ReadAsStringAsync())
            .Should().Be(WithoutCorrelation(await nobody.Content.ReadAsStringAsync()));
    }

    /// <summary>The ordinary path: ask, release, see.</summary>
    [Fact]
    public async Task Ask_release_see()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Set(AsPerson(anna), "listening", remark: "Remote only.");
        _ledger.ProfileReleased.Add(anna);

        var asked = await AsCompany(company).PostAsync($"/market/{anna}/requests", null);

        asked.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(asked)).GetProperty("status").GetString().Should().Be("PENDING");

        var id = (await Json(asked)).GetProperty("id").GetGuid();

        var granted = await AsPerson(anna).PostAsync($"/market/requests/{id}/grant", null);

        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        _ledger.Granted.Should().ContainSingle().Which.Should().Be((anna, company));

        var seen = await AsCompany(company).GetAsync($"/market/{anna}");

        seen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(seen)).GetProperty("note").GetString().Should().Be("Remote only.");
    }

    /// <summary>
    /// The request presupposes the PROFILE release, not the existence of a
    /// market status.
    /// </summary>
    /// <remarks>
    /// Checking both would be an oracle: "has already kept a market status" is
    /// information about the person that nobody should be able to ask for.
    /// </remarks>
    [Fact]
    public async Task Without_a_profile_release_nobody_is_there()
    {
        var anna = Guid.CreateVersion7();
        await Set(AsPerson(anna), "open");

        var asked = await AsCompany(Guid.CreateVersion7())
            .PostAsync($"/market/{anna}/requests", null);

        asked.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Whoever has never kept a market status looks exactly like somebody who
    /// has one.
    /// </summary>
    [Fact]
    public async Task Asking_works_even_without_a_status()
    {
        var nobody = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(nobody);

        var asked = await AsCompany(Guid.CreateVersion7())
            .PostAsync($"/market/{nobody}/requests", null);

        asked.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Ask once. Whoever may ask three times has not got a no but a delay.
    /// </summary>
    [Fact]
    public async Task A_company_asks_only_once()
    {
        var anna = Guid.CreateVersion7();
        var company = AsCompany(Guid.CreateVersion7());
        _ledger.ProfileReleased.Add(anna);

        await company.PostAsync($"/market/{anna}/requests", null);
        var again = await company.PostAsync($"/market/{anna}/requests", null);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// After a refusal too — the revocation is a stronger statement than the
    /// refusal, not a weaker one.
    /// </summary>
    [Fact]
    public async Task After_a_refusal_nobody_asks_again_either()
    {
        var anna = Guid.CreateVersion7();
        var company = AsCompany(Guid.CreateVersion7());
        _ledger.ProfileReleased.Add(anna);

        var id = (await Json(await company.PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await AsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        var again = await company.PostAsync($"/market/{anna}/requests", null);

        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>The refusal revokes in the ledger too.</summary>
    /// <remarks>
    /// If the ledger call succeeded and the commit then failed, a permission
    /// would otherwise exist without a visible case — the one way in which this
    /// system could fail OPEN to the outside.
    /// </remarks>
    [Fact]
    public async Task The_refusal_revokes_too()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        await AsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        _ledger.Revoked.Should().ContainSingle().Which.Should().Be((anna, company));
    }

    /// <summary>
    /// A second "grant" after a "decline" would silently reverse the refusal.
    /// </summary>
    [Fact]
    public async Task An_answered_request_cannot_be_reversed()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(Guid.CreateVersion7())
                .PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        var asAnna = AsPerson(anna);
        await asAnna.PostAsync($"/market/requests/{id}/decline", null);

        var reversal = await asAnna.PostAsync($"/market/requests/{id}/grant", null);

        reversal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Only the person asked answers — and a stranger sees 404.</summary>
    [Fact]
    public async Task A_stranger_does_not_answer_the_request()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);

        var id = (await Json(await AsCompany(Guid.CreateVersion7())
                .PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        var stranger = await AsPerson(Guid.CreateVersion7())
            .PostAsync($"/market/requests/{id}/grant", null);

        stranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// After the revocation the request stays GRANTED and `active` falls to
    /// false.
    /// </summary>
    /// <remarks>
    /// That is exactly why the permission does not stand in the case: "was
    /// granted once" and "holds now" are two different statements.
    /// </remarks>
    [Fact]
    public async Task After_the_revocation_GRANTED_stays_and_active_falls()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);
        var asAnna = AsPerson(anna);

        var id = (await Json(await AsCompany(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await asAnna.PostAsync($"/market/requests/{id}/grant", null);

        var before = (await Json(await asAnna.GetAsync("/market/me/requests")))
            .EnumerateArray().Single();
        before.GetProperty("status").GetString().Should().Be("GRANTED");
        before.GetProperty("active").GetBoolean().Should().BeTrue();

        var revoked = await asAnna.PostAsync($"/market/requests/{id}/revoke", null);

        // The status code has to be checked along with it: a counter-probe
        // showed that a revocation answering 500 would otherwise pass here —
        // the rollback would run silently into nothing and the state would look
        // right.
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await Json(await asAnna.GetAsync("/market/me/requests")))
            .EnumerateArray().Single();
        after.GetProperty("status").GetString().Should().Be("GRANTED");
        after.GetProperty("active").GetBoolean().Should().BeFalse();

        (await AsCompany(company).GetAsync($"/market/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The asking company gets no `active`.
    /// </summary>
    /// <remarks>
    /// It has the answer already in the form of the status it sees or does not
    /// see — and a field here could be queried without ever reading a market
    /// status.
    /// </remarks>
    [Fact]
    public async Task The_company_sees_no_active()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);
        var company = AsCompany(Guid.CreateVersion7());

        await company.PostAsync($"/market/{anna}/requests", null);

        var theirs = (await Json(await company.GetAsync("/market/requests")))
            .EnumerateArray().Single();

        theirs.GetProperty("active").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Refused ones stay visible to the company too — otherwise "refused" and
    /// "never asked" would look the same.
    /// </summary>
    [Fact]
    public async Task Refused_requests_stay_visible()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);
        var company = AsCompany(Guid.CreateVersion7());

        var id = (await Json(await company.PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await AsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        var theirs = (await Json(await company.GetAsync("/market/requests")))
            .EnumerateArray().Single();

        theirs.GetProperty("status").GetString().Should().Be("DECLINED");
    }

    /// <summary>The request leaves its intent in the outbox.</summary>
    [Fact]
    public async Task The_request_leaves_the_intent_in_the_outbox()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfileReleased.Add(anna);

        await AsCompany(Guid.CreateVersion7()).PostAsync($"/market/{anna}/requests", null);

        await using var context = postgres.Context();
        var notes = await context.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        notes.Should().ContainSingle().Which.Kind.Should().Be("market_request");
    }

    /// <summary>
    /// If the ledger is silent, no request comes into being — and the answer is
    /// 503.
    /// </summary>
    [Fact]
    public async Task A_silent_ledger_leaves_no_request_behind()
    {
        var anna = Guid.CreateVersion7();
        _ledger.Silent = true;

        var response = await AsCompany(Guid.CreateVersion7())
            .PostAsync($"/market/{anna}/requests", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Context();
        (await context.Requests.AnyAsync(row => row.SubjectId == anna))
            .Should().BeFalse();
    }

    /// <summary>A market status is read only with a company.</summary>
    [Fact]
    public async Task A_private_person_reads_no_foreign_market_status()
    {
        var response = await AsPerson(Guid.CreateVersion7())
            .GetAsync($"/market/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Without signing in there is nothing.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        (await _service.CreateClient().GetAsync("/market/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
