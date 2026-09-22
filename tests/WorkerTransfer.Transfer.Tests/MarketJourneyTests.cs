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

/// <summary>Der MarketStatus, seine Release und ihr Revocation.</summary>
[Collection(PostgresCollection.Name)]
public class MarketJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly ProbeLedger _ledger = new();

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
                // Angebot und Abschluss verlangen seit PBI-2 einen `admin`.
                // Diese Reihen messen den Case und nicht die Rolle — also
                // antwortet die Probe wie eine Inhaberin. `RollenTests` dreht
                // sie auf `member`.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid company) => Mit(TokenShape.Company(Guid.CreateVersion7(), company));

    private HttpClient AlsPerson(Guid subject) => Mit(TokenShape.Person(subject));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Der Problemrumpf ohne die Korrelationskennung.</summary>
    private static string OhneKorrelation(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body, "\"correlationId\":\"[^\"]*\",?", string.Empty);

    private static Task<HttpResponseMessage> Set(
        HttpClient browser, string state, bool isEmployed = false, string notiz = "") =>
        browser.PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = isEmployed, note = notiz
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
        var response = await AlsPerson(Guid.CreateVersion7()).GetAsync("/market/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await Json(response);
        status.GetProperty("availability").GetString().Should().Be("unavailable");
        status.GetProperty("is_approachable").GetBoolean().Should().BeFalse();
    }

    /// <summary>Every transition is allowed — it is a statement about the will.</summary>
    [Fact]
    public async Task Every_state_can_be_changed_into_every_other()
    {
        var browser = AlsPerson(Guid.CreateVersion7());

        foreach (var state in new[] { "listening", "unavailable", "open", "listening" })
        {
            var response = await Set(browser, state);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Json(response)).GetProperty("availability").GetString().Should().Be(state);
        }
    }

    /// <summary>Ein Wort, das es nicht gibt, ist 422 und kein Absturz.</summary>
    [Fact]
    public async Task An_invented_state_is_422()
    {
        var response = await Set(AlsPerson(Guid.CreateVersion7()), "vielleicht");

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
        await Set(AlsPerson(anna), "open");

        var company = AlsFirma(Guid.CreateVersion7());

        var verborgen = await company.GetAsync($"/market/{anna}");
        var niemand = await company.GetAsync($"/market/{Guid.CreateVersion7()}");

        verborgen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        niemand.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Byte-identical apart from the correlation id — that differs per
        // request and says nothing about the person.
        OhneKorrelation(await verborgen.Content.ReadAsStringAsync())
            .Should().Be(OhneKorrelation(await niemand.Content.ReadAsStringAsync()));
    }

    /// <summary>The ordinary path: ask, release, see.</summary>
    [Fact]
    public async Task Ask_release_see()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Set(AlsPerson(anna), "listening", notiz: "Nur Remote.");
        _ledger.ProfilFrei.Add(anna);

        var gefragt = await AlsFirma(company).PostAsync($"/market/{anna}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(gefragt)).GetProperty("status").GetString().Should().Be("PENDING");

        var id = (await Json(gefragt)).GetProperty("id").GetGuid();

        var granted = await AlsPerson(anna).PostAsync($"/market/requests/{id}/grant", null);

        granted.StatusCode.Should().Be(HttpStatusCode.OK);
        _ledger.Erteilt.Should().ContainSingle().Which.Should().Be((anna, company));

        var gesehen = await AlsFirma(company).GetAsync($"/market/{anna}");

        gesehen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(gesehen)).GetProperty("note").GetString().Should().Be("Nur Remote.");
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
        await Set(AlsPerson(anna), "open");

        var gefragt = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/market/{anna}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Wer noch nie einen MarketStatus gepflegt hat, sieht genauso aus wie
    /// jemand, der einen hat.
    /// </summary>
    [Fact]
    public async Task Asking_works_even_without_a_status()
    {
        var niemand = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(niemand);

        var gefragt = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/market/{niemand}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Ask once. Whoever may ask three times has not got a no but a delay.
    /// </summary>
    [Fact]
    public async Task A_company_asks_only_once()
    {
        var anna = Guid.CreateVersion7();
        var company = AlsFirma(Guid.CreateVersion7());
        _ledger.ProfilFrei.Add(anna);

        await company.PostAsync($"/market/{anna}/requests", null);
        var nochmal = await company.PostAsync($"/market/{anna}/requests", null);

        nochmal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// After a refusal too — the revocation is a stronger statement than the
    /// refusal, not a weaker one.
    /// </summary>
    [Fact]
    public async Task After_a_refusal_nobody_asks_again_either()
    {
        var anna = Guid.CreateVersion7();
        var company = AlsFirma(Guid.CreateVersion7());
        _ledger.ProfilFrei.Add(anna);

        var id = (await Json(await company.PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await AlsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        var nochmal = await company.PostAsync($"/market/{anna}/requests", null);

        nochmal.StatusCode.Should().Be(HttpStatusCode.Conflict);
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
        _ledger.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        await AlsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        _ledger.Widerrufen.Should().ContainSingle().Which.Should().Be((anna, company));
    }

    /// <summary>
    /// A second "grant" after a "decline" would silently reverse the refusal.
    /// </summary>
    [Fact]
    public async Task An_answered_request_cannot_be_reversed()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(Guid.CreateVersion7())
                .PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        var ihr = AlsPerson(anna);
        await ihr.PostAsync($"/market/requests/{id}/decline", null);

        var umdrehen = await ihr.PostAsync($"/market/requests/{id}/grant", null);

        umdrehen.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Nur die gefragte Person antwortet — und ein Fremder sieht 404.</summary>
    [Fact]
    public async Task A_stranger_does_not_answer_the_request()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);

        var id = (await Json(await AlsFirma(Guid.CreateVersion7())
                .PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();

        var fremd = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/market/requests/{id}/grant", null);

        fremd.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
        _ledger.ProfilFrei.Add(anna);
        var ihr = AlsPerson(anna);

        var id = (await Json(await AlsFirma(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await ihr.PostAsync($"/market/requests/{id}/grant", null);

        var vorher = (await Json(await ihr.GetAsync("/market/me/requests")))
            .EnumerateArray().Single();
        vorher.GetProperty("status").GetString().Should().Be("GRANTED");
        vorher.GetProperty("active").GetBoolean().Should().BeTrue();

        var widerrufen = await ihr.PostAsync($"/market/requests/{id}/revoke", null);

        // The status code has to be checked along with it: a counter-probe
        // showed that a revocation answering 500 would otherwise pass here —
        // the rollback would run silently into nothing and the state would look
        // right.
        widerrufen.StatusCode.Should().Be(HttpStatusCode.OK);

        var nachher = (await Json(await ihr.GetAsync("/market/me/requests")))
            .EnumerateArray().Single();
        nachher.GetProperty("status").GetString().Should().Be("GRANTED");
        nachher.GetProperty("active").GetBoolean().Should().BeFalse();

        (await AlsFirma(company).GetAsync($"/market/{anna}"))
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
        _ledger.ProfilFrei.Add(anna);
        var company = AlsFirma(Guid.CreateVersion7());

        await company.PostAsync($"/market/{anna}/requests", null);

        var ihre = (await Json(await company.GetAsync("/market/requests")))
            .EnumerateArray().Single();

        ihre.GetProperty("active").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Refused ones stay visible to the company too — otherwise "refused" and
    /// "never asked" would look the same.
    /// </summary>
    [Fact]
    public async Task Refused_requests_stay_visible()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);
        var company = AlsFirma(Guid.CreateVersion7());

        var id = (await Json(await company.PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await AlsPerson(anna).PostAsync($"/market/requests/{id}/decline", null);

        var ihre = (await Json(await company.GetAsync("/market/requests")))
            .EnumerateArray().Single();

        ihre.GetProperty("status").GetString().Should().Be("DECLINED");
    }

    /// <summary>The request leaves its intent in the outbox.</summary>
    [Fact]
    public async Task The_request_leaves_the_intent_in_the_outbox()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);

        await AlsFirma(Guid.CreateVersion7()).PostAsync($"/market/{anna}/requests", null);

        await using var context = postgres.Context();
        var vermerke = await context.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        vermerke.Should().ContainSingle().Which.Kind.Should().Be("market_request");
    }

    /// <summary>
    /// Schweigt der Ledger, entsteht keine ResumeRequest — und die Antwort ist 503.
    /// </summary>
    [Fact]
    public async Task A_silent_ledger_leaves_no_request_behind()
    {
        var anna = Guid.CreateVersion7();
        _ledger.Schweigt = true;

        var response = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/market/{anna}/requests", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Context();
        (await context.Requests.AnyAsync(row => row.SubjectId == anna))
            .Should().BeFalse();
    }

    /// <summary>Ein MarketStatus liest sich nur mit einem Unternehmen.</summary>
    [Fact]
    public async Task A_private_person_reads_no_foreign_market_status()
    {
        var response = await AlsPerson(Guid.CreateVersion7())
            .GetAsync($"/market/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung gibt es nichts.</summary>
    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        (await _dienst.CreateClient().GetAsync("/market/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
