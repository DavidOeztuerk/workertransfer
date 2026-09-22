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

/// <summary>Der Case: drei Ja, jederzeit ein Nein.</summary>
[Collection(PostgresCollection.Name)]
public class CaseJourneyTests(Postgres postgres) : IAsyncLifetime
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

    /// <summary>Eine ansprechbare Person, deren Status dieser Firma freigegeben ist.</summary>
    private async Task<Guid> Ansprechbar(
        Guid company, bool isEmployed = false, string state = "listening")
    {
        var subject = Guid.CreateVersion7();

        await AlsPerson(subject).PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = isEmployed, note = string.Empty
        });

        _ledger.MarktFrei.Add((subject, company));

        return subject;
    }

    private Task<HttpResponseMessage> ZeigeInteresse(Guid company, Guid subject) =>
        AlsFirma(company).PostAsJsonAsync("/transfers", new
        {
            subject_id = subject, message = "Wir hätten da etwas."
        });

    /// <summary>The whole path: interest, conversation, offer, acceptance, completion.</summary>
    [Fact]
    public async Task Interesse_Gespraech_Angebot_Annahme_Abschluss()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var ihr = AlsPerson(anna);
        var sie = AlsFirma(company);

        var angelegt = await ZeigeInteresse(company, anna);

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(angelegt)).GetProperty("status").GetString().Should().Be("interested");

        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await ihr.PostAsync($"/transfers/{id}/accept-talk", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var angeboten = await sie.PostAsJsonAsync($"/transfers/{id}/offer", new
        {
            note = "Wir bieten die Stelle an.", start_on = "2026-10", fee_cents = 250_000L
        });

        angeboten.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(angeboten)).GetProperty("offer_fee_cents").GetInt64().Should().Be(250_000);

        (await ihr.PostAsync($"/transfers/{id}/accept-offer", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var fertig = await sie.PostAsync($"/transfers/{id}/complete", null);

        fertig.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(fertig)).GetProperty("status").GetString().Should().Be("completed");
    }

    /// <summary>
    /// Whoever is employed completes themselves — they are the only one who
    /// knows whether the release is there.
    /// </summary>
    [Fact]
    public async Task With_a_release_requirement_the_person_completes()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, isEmployed: true);
        var ihr = AlsPerson(anna);
        var sie = AlsFirma(company);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        (await Json(await ihr.GetAsync("/transfers/me")))
            .EnumerateArray().Single()
            .GetProperty("requires_release").GetBoolean().Should().BeTrue();

        await ihr.PostAsync($"/transfers/{id}/accept-talk", null);
        await sie.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Angebot." });
        await ihr.PostAsync($"/transfers/{id}/accept-offer", null);

        // The company may NOT complete now.
        var zuFrueh = await sie.PostAsync($"/transfers/{id}/complete", null);

        zuFrueh.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var bestaetigt = await ihr.PostAsync($"/transfers/{id}/confirm-release", null);

        bestaetigt.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(bestaetigt);
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("release_confirmed").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Without a release requirement there is nothing to confirm.
    /// </summary>
    [Fact]
    public async Task Without_a_release_requirement_there_is_nothing_to_confirm()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var ihr = AlsPerson(anna);
        var sie = AlsFirma(company);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();
        await ihr.PostAsync($"/transfers/{id}/accept-talk", null);
        await sie.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Angebot." });
        await ihr.PostAsync($"/transfers/{id}/accept-offer", null);

        var unnoetig = await ihr.PostAsync($"/transfers/{id}/confirm-release", null);

        unnoetig.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// A later change of the market status does not shift the terms of a
    /// running case retroactively.
    /// </summary>
    [Fact]
    public async Task The_release_requirement_is_copied_at_creation()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, isEmployed: true);
        var ihr = AlsPerson(anna);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        // They resign in the middle of the conversation.
        await ihr.PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        var now = (await Json(await ihr.GetAsync("/transfers/me")))
            .EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

        now.GetProperty("requires_release").GetBoolean().Should().BeTrue();
    }

    /// <summary>The release requirement has no setter.</summary>
    /// <remarks>
    /// Not "is not changed" but <em>cannot</em> be: there is no assignment for
    /// it, and therefore no later move can shift the terms of a running case
    /// either. This check falls the moment somebody adds one — a counter-probe
    /// showed that this cannot be refuted over HTTP for as long as the setter is
    /// missing.
    /// </remarks>
    [Fact]
    public void Die_Freigabepflicht_hat_keinen_Setzer()
    {
        typeof(Domain.Cases.Transfer)
            .GetProperty(nameof(Domain.Cases.Transfer.BrauchtFreigabe))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>Die Release erlaubt zu SEHEN, nicht zu STÖREN.</summary>
    [Fact]
    public async Task Unavailable_means_no_even_with_a_release()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, state: "unavailable");

        var attempt = await ZeigeInteresse(company, anna);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// No status, no release, or "not right now" — all the same to the
    /// outside.
    /// </summary>
    /// <remarks>
    /// Otherwise the endpoint would be an oracle about who is on the platform
    /// and who is listening right now.
    /// </remarks>
    [Fact]
    public async Task The_three_nos_look_the_same()
    {
        var company = Guid.CreateVersion7();
        var ohneStatus = Guid.CreateVersion7();
        var ohneFreigabe = Guid.CreateVersion7();
        await AlsPerson(ohneFreigabe).PutAsJsonAsync("/market/me", new
        {
            availability = "open", employed = false, note = string.Empty
        });
        var nichtAnsprechbar = await Ansprechbar(company, state: "unavailable");

        var responses = new List<string>();

        foreach (var subject in new[] { ohneStatus, ohneFreigabe, nichtAnsprechbar })
        {
            var response = await ZeigeInteresse(company, subject);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            responses.Add(
                (await Json(response)).GetProperty("detail").GetString() ?? string.Empty);
        }

        responses.Distinct().Should().ContainSingle();
    }

    /// <summary>
    /// A second running case would be following up past the refusal.
    /// </summary>
    [Fact]
    public async Task Only_one_case_runs_per_pair()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);

        await ZeigeInteresse(company, anna);
        var zweiter = await ZeigeInteresse(company, anna);

        zweiter.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Nach einer Absage darf ein neuer beginnen.</summary>
    [Fact]
    public async Task After_the_refusal_a_new_one_may_begin()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();
        await AlsPerson(anna).PostAsync($"/transfers/{id}/decline", null);

        var neuer = await ZeigeInteresse(company, anna);

        neuer.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(neuer)).GetProperty("id").GetGuid().Should().NotBe(id);
    }

    /// <summary>
    /// Declining is possible from every running state — a process one cannot
    /// get out of is a trap.
    /// </summary>
    [Fact]
    public async Task Declining_works_from_every_running_state()
    {
        var company = Guid.CreateVersion7();
        var sie = AlsFirma(company);

        foreach (var bisWohin in new[] { "interested", "talking", "offered", "accepted" })
        {
            var anna = await Ansprechbar(company);
            var ihr = AlsPerson(anna);
            var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

            if (bisWohin != "interested")
            {
                await ihr.PostAsync($"/transfers/{id}/accept-talk", null);
            }

            if (bisWohin is "offered" or "accepted")
            {
                await sie.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Angebot." });
            }

            if (bisWohin == "accepted")
            {
                await ihr.PostAsync($"/transfers/{id}/accept-offer", null);
            }

            var abgesagt = await ihr.PostAsync($"/transfers/{id}/decline", null);

            abgesagt.StatusCode.Should().Be(HttpStatusCode.OK, $"aus {bisWohin} heraus");
            (await Json(abgesagt)).GetProperty("status").GetString().Should().Be("declined");
        }
    }

    /// <summary>Somebody else's case is like none from outside.</summary>
    [Fact]
    public async Task Somebody_elses_case_is_404_and_not_403()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        var fremdePerson = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/transfers/{id}/accept-talk", null);
        var fremdeFirma = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/transfers/{id}/withdraw", null);

        fremdePerson.StatusCode.Should().Be(HttpStatusCode.NotFound);
        fremdeFirma.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>There is no negative fee.</summary>
    [Fact]
    public async Task A_negative_fee_is_refused()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();
        await AlsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        var angebot = await AlsFirma(company).PostAsJsonAsync($"/transfers/{id}/offer", new
        {
            note = "Angebot.", fee_cents = -1L
        });

        angebot.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Only the company's moves are notified.</summary>
    /// <remarks>
    /// The person learns nothing from the company by mail: it is the side that
    /// wants something, and a mail to a company distribution list carrying a
    /// human being's name would be the same leak channel, only the other way
    /// round.
    /// </remarks>
    [Fact]
    public async Task Only_the_companys_moves_are_notified()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        // Interesse: gemeldet.
        await using (var nachInteresse = postgres.Context())
        {
            (await nachInteresse.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Ihr Zug: nicht gemeldet.
        await AlsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        await using (var nachIhremZug = postgres.Context())
        {
            (await nachIhremZug.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Ihr Angebot: gemeldet.
        await AlsFirma(company).PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Ja." });

        await using var nachAngebot = postgres.Context();
        var vermerke = await nachAngebot.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        vermerke.Should().HaveCount(2);
        vermerke.Should().OnlyContain(row => row.Kind == "transfer_update");
    }

    /// <summary>
    /// The revocation does NOT end the running case: it has a door and a
    /// refusal of its own.
    /// </summary>
    [Fact]
    public async Task A_revocation_does_not_end_the_case()
    {
        var company = Guid.CreateVersion7();
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);
        var ihr = AlsPerson(anna);

        await ihr.PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        var request = (await Json(
                await AlsFirma(company).PostAsync($"/market/{anna}/requests", null)))
            .GetProperty("id").GetGuid();
        await ihr.PostAsync($"/market/requests/{request}/grant", null);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        await ihr.PostAsync($"/market/requests/{request}/revoke", null);

        var laeuftNoch = (await Json(await ihr.GetAsync("/transfers/me")))
            .EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

        laeuftNoch.GetProperty("status").GetString().Should().Be("interested");
    }

    /// <summary>Interesse zeigt nur ein Unternehmen.</summary>
    [Fact]
    public async Task A_private_person_shows_no_interest()
    {
        var response = await AlsPerson(Guid.CreateVersion7()).PostAsJsonAsync("/transfers", new
        {
            subject_id = Guid.CreateVersion7(), message = "Hallo."
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
