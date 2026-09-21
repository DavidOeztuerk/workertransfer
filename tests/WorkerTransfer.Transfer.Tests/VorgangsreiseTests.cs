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
public class VorgangsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:transfer", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
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
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new Rollenprobe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid company) => Mit(Tokenform.Firma(Guid.CreateVersion7(), company));

    private HttpClient AlsPerson(Guid subject) => Mit(Tokenform.Person(subject));

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
        Guid company, bool beschaeftigt = false, string state = "listening")
    {
        var subject = Guid.CreateVersion7();

        await AlsPerson(subject).PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = beschaeftigt, note = string.Empty
        });

        _ledger.MarktFrei.Add((subject, company));

        return subject;
    }

    private Task<HttpResponseMessage> ZeigeInteresse(Guid company, Guid subject) =>
        AlsFirma(company).PostAsJsonAsync("/transfers", new
        {
            subject_id = subject, message = "Wir hätten da etwas."
        });

    /// <summary>Der ganze Weg: Interesse, Gespräch, Angebot, Annahme, Abschluss.</summary>
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
    /// Wer beschäftigt ist, schließt selbst ab — sie ist die Einzige, die weiß,
    /// ob die Release vorliegt.
    /// </summary>
    [Fact]
    public async Task Bei_Freigabepflicht_schliesst_die_Person_ab()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, beschaeftigt: true);
        var ihr = AlsPerson(anna);
        var sie = AlsFirma(company);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        (await Json(await ihr.GetAsync("/transfers/me")))
            .EnumerateArray().Single()
            .GetProperty("requires_release").GetBoolean().Should().BeTrue();

        await ihr.PostAsync($"/transfers/{id}/accept-talk", null);
        await sie.PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Angebot." });
        await ihr.PostAsync($"/transfers/{id}/accept-offer", null);

        // Das Unternehmen darf jetzt NICHT abschließen.
        var zuFrueh = await sie.PostAsync($"/transfers/{id}/complete", null);

        zuFrueh.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var bestaetigt = await ihr.PostAsync($"/transfers/{id}/confirm-release", null);

        bestaetigt.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(bestaetigt);
        body.GetProperty("status").GetString().Should().Be("completed");
        body.GetProperty("release_confirmed").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Ohne Freigabepflicht gibt es nichts zu bestätigen.
    /// </summary>
    [Fact]
    public async Task Ohne_Freigabepflicht_gibt_es_nichts_zu_bestaetigen()
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
    /// Ein späterer Wechsel des MarketStatus verschiebt die Bedingungen eines
    /// laufenden Vorgangs nicht rückwirkend.
    /// </summary>
    [Fact]
    public async Task Die_Freigabepflicht_wird_beim_Anlegen_kopiert()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, beschaeftigt: true);
        var ihr = AlsPerson(anna);

        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        // Sie kündigt mitten im Gespräch.
        await ihr.PutAsJsonAsync("/market/me", new
        {
            availability = "listening", employed = false, note = string.Empty
        });

        var now = (await Json(await ihr.GetAsync("/transfers/me")))
            .EnumerateArray().Single(entry => entry.GetProperty("id").GetGuid() == id);

        now.GetProperty("requires_release").GetBoolean().Should().BeTrue();
    }

    /// <summary>Die Freigabepflicht hat keinen Setzer.</summary>
    /// <remarks>
    /// Nicht „wird nicht geändert", sondern <em>kann</em> nicht: es gibt keine
    /// Zuweisung dafür, und deshalb kann auch kein späterer Zug die Bedingungen
    /// eines laufenden Vorgangs verschieben. Diese Prüfung fällt, sobald jemand
    /// eine hinzufügt — eine Gegenprobe hat gezeigt, dass sich das über HTTP
    /// nicht widerlegen lässt, solange der Setzer fehlt.
    /// </remarks>
    [Fact]
    public void Die_Freigabepflicht_hat_keinen_Setzer()
    {
        typeof(Domain.Vorgaenge.Transfer)
            .GetProperty(nameof(Domain.Vorgaenge.Transfer.BrauchtFreigabe))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>Die Release erlaubt zu SEHEN, nicht zu STÖREN.</summary>
    [Fact]
    public async Task Unavailable_heisst_nein_auch_mit_Freigabe()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company, state: "unavailable");

        var attempt = await ZeigeInteresse(company, anna);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Kein Status, keine Release, oder „gerade nicht" — alles dasselbe nach
    /// außen.
    /// </summary>
    /// <remarks>
    /// Sonst wäre der Endpunkt ein Orakel darüber, wer auf der Plattform ist
    /// und wer gerade zuhört.
    /// </remarks>
    [Fact]
    public async Task Die_drei_Nein_sehen_gleich_aus()
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
    /// Ein zweiter laufender Case wäre Nachfassen an der Absage vorbei.
    /// </summary>
    [Fact]
    public async Task Es_laeuft_nur_ein_Vorgang_je_Paar()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);

        await ZeigeInteresse(company, anna);
        var zweiter = await ZeigeInteresse(company, anna);

        zweiter.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Nach einer Absage darf ein neuer beginnen.</summary>
    [Fact]
    public async Task Nach_der_Absage_darf_ein_neuer_beginnen()
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
    /// Absagen ist aus jedem laufenden Zustand möglich — ein Verfahren, aus dem
    /// man nicht aussteigen kann, ist eine Falle.
    /// </summary>
    [Fact]
    public async Task Absagen_geht_aus_jedem_laufenden_Zustand()
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

    /// <summary>Ein fremder Case ist von außen wie keiner.</summary>
    [Fact]
    public async Task Ein_fremder_Vorgang_ist_404_und_nicht_403()
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

    /// <summary>Eine negative Ablöse gibt es nicht.</summary>
    [Fact]
    public async Task Eine_negative_Abloese_wird_abgewiesen()
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

    /// <summary>Nur Züge des Unternehmens werden gemeldet.</summary>
    /// <remarks>
    /// Die Person erfährt vom Unternehmen nichts per Mail: es ist die Seite,
    /// die etwas will, und eine Mail an einen Firmenverteiler mit dem Namen
    /// eines Menschen wäre derselbe Leck-Kanal, nur andersherum.
    /// </remarks>
    [Fact]
    public async Task Nur_die_Zuege_des_Unternehmens_werden_gemeldet()
    {
        var company = Guid.CreateVersion7();
        var anna = await Ansprechbar(company);
        var id = (await Json(await ZeigeInteresse(company, anna))).GetProperty("id").GetGuid();

        // Interesse: gemeldet.
        await using (var nachInteresse = postgres.Kontext())
        {
            (await nachInteresse.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Ihr Zug: nicht gemeldet.
        await AlsPerson(anna).PostAsync($"/transfers/{id}/accept-talk", null);

        await using (var nachIhremZug = postgres.Kontext())
        {
            (await nachIhremZug.Set<OutboxRow>().CountAsync(row => row.UserId == anna))
                .Should().Be(1);
        }

        // Ihr Angebot: gemeldet.
        await AlsFirma(company).PostAsJsonAsync($"/transfers/{id}/offer", new { note = "Ja." });

        await using var nachAngebot = postgres.Kontext();
        var vermerke = await nachAngebot.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        vermerke.Should().HaveCount(2);
        vermerke.Should().OnlyContain(row => row.Kind == "transfer_update");
    }

    /// <summary>
    /// Der Revocation beendet den laufenden Case NICHT: er hat seine eigene
    /// Tür und seine eigene Absage.
    /// </summary>
    [Fact]
    public async Task Ein_Widerruf_beendet_den_Vorgang_nicht()
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
    public async Task Eine_Privatperson_zeigt_kein_Interesse()
    {
        var response = await AlsPerson(Guid.CreateVersion7()).PostAsJsonAsync("/transfers", new
        {
            subject_id = Guid.CreateVersion7(), message = "Hallo."
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
