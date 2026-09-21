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
public class MarktreiseTests(Postgres postgres) : IAsyncLifetime
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

    /// <summary>Der Problemrumpf ohne die Korrelationskennung.</summary>
    private static string OhneKorrelation(string body) =>
        System.Text.RegularExpressions.Regex.Replace(
            body, "\"correlationId\":\"[^\"]*\",?", string.Empty);

    private static Task<HttpResponseMessage> Set(
        HttpClient browser, string state, bool beschaeftigt = false, string notiz = "") =>
        browser.PutAsJsonAsync("/market/me", new
        {
            availability = state, employed = beschaeftigt, note = notiz
        });

    /// <summary>
    /// Wer nichts gesagt hat, hat nicht „ich höre zu" gesagt.
    /// </summary>
    /// <remarks>
    /// Die Voreinstellung darf nie zugunsten des Marktes ausfallen — und sie
    /// ist nie <c>null</c>: „nichts gesagt" IST ein Zustand.
    /// </remarks>
    [Fact]
    public async Task Wer_nichts_gesagt_hat_ist_nicht_verfuegbar()
    {
        var response = await AlsPerson(Guid.CreateVersion7()).GetAsync("/market/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await Json(response);
        status.GetProperty("availability").GetString().Should().Be("unavailable");
        status.GetProperty("is_approachable").GetBoolean().Should().BeFalse();
    }

    /// <summary>Alle Übergänge sind erlaubt — es ist eine Aussage über den Willen.</summary>
    [Fact]
    public async Task Jeder_Zustand_laesst_sich_in_jeden_anderen_aendern()
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
    public async Task Ein_erfundener_Zustand_ist_422()
    {
        var response = await Set(AlsPerson(Guid.CreateVersion7()), "vielleicht");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Ohne Release sieht ein Unternehmen nichts — und „nicht vorhanden"
    /// sieht genauso aus.
    /// </summary>
    /// <remarks>
    /// Der Unterschied wäre hier besonders teuer: schon die Existenz der
    /// Aussage verrät etwas.
    /// </remarks>
    [Fact]
    public async Task Verborgen_und_nicht_vorhanden_antworten_gleich()
    {
        var anna = Guid.CreateVersion7();
        await Set(AlsPerson(anna), "open");

        var company = AlsFirma(Guid.CreateVersion7());

        var verborgen = await company.GetAsync($"/market/{anna}");
        var niemand = await company.GetAsync($"/market/{Guid.CreateVersion7()}");

        verborgen.StatusCode.Should().Be(HttpStatusCode.NotFound);
        niemand.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Bis auf die Korrelationskennung buchstabengleich — die ist je
        // ResumeRequest verschieden und sagt über die Person nichts.
        OhneKorrelation(await verborgen.Content.ReadAsStringAsync())
            .Should().Be(OhneKorrelation(await niemand.Content.ReadAsStringAsync()));
    }

    /// <summary>Der gewöhnliche Weg: fragen, freigeben, sehen.</summary>
    [Fact]
    public async Task Fragen_freigeben_sehen()
    {
        var anna = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Set(AlsPerson(anna), "listening", notiz: "Nur Remote.");
        _ledger.ProfilFrei.Add(anna);

        var gefragt = await AlsFirma(company).PostAsync($"/market/{anna}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(gefragt)).GetProperty("status").GetString().Should().Be("PENDING");

        var id = (await Json(gefragt)).GetProperty("id").GetGuid();

        var erteilt = await AlsPerson(anna).PostAsync($"/market/requests/{id}/grant", null);

        erteilt.StatusCode.Should().Be(HttpStatusCode.OK);
        _ledger.Erteilt.Should().ContainSingle().Which.Should().Be((anna, company));

        var gesehen = await AlsFirma(company).GetAsync($"/market/{anna}");

        gesehen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(gesehen)).GetProperty("note").GetString().Should().Be("Nur Remote.");
    }

    /// <summary>
    /// Die ResumeRequest setzt die PROFILfreigabe voraus, nicht die Existenz eines
    /// MarketStatus.
    /// </summary>
    /// <remarks>
    /// Beides zu prüfen wäre ein Orakel: „hat schon einen MarketStatus gepflegt"
    /// ist eine Information über die Person, die niemand erfragen können soll.
    /// </remarks>
    [Fact]
    public async Task Ohne_Profilfreigabe_ist_niemand_da()
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
    public async Task Fragen_geht_auch_ohne_dass_es_einen_Status_gibt()
    {
        var niemand = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(niemand);

        var gefragt = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/market/{niemand}/requests", null);

        gefragt.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Einmal fragen. Wer dreimal fragen darf, hat kein Nein bekommen, sondern
    /// eine Verzögerung.
    /// </summary>
    [Fact]
    public async Task Ein_Unternehmen_fragt_nur_einmal()
    {
        var anna = Guid.CreateVersion7();
        var company = AlsFirma(Guid.CreateVersion7());
        _ledger.ProfilFrei.Add(anna);

        await company.PostAsync($"/market/{anna}/requests", null);
        var nochmal = await company.PostAsync($"/market/{anna}/requests", null);

        nochmal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Auch nach einer Ablehnung — der Revocation ist eine stärkere Aussage als
    /// die Ablehnung, nicht eine schwächere.
    /// </summary>
    [Fact]
    public async Task Auch_nach_der_Ablehnung_wird_nicht_erneut_gefragt()
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

    /// <summary>Auch die Ablehnung widerruft im Ledger.</summary>
    /// <remarks>
    /// Gelänge der Ledger-Aufruf und scheiterte danach der Commit, existierte
    /// sonst eine Berechtigung ohne sichtbaren Case — der einzige Weg, auf
    /// dem dieses System nach außen OFFEN scheitern könnte.
    /// </remarks>
    [Fact]
    public async Task Die_Ablehnung_widerruft_auch()
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
    /// Ein zweites „grant" nach einem „decline" würde die Ablehnung
    /// stillschweigend umdrehen.
    /// </summary>
    [Fact]
    public async Task Eine_beantwortete_Anfrage_laesst_sich_nicht_umdrehen()
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
    public async Task Ein_Fremder_beantwortet_die_Anfrage_nicht()
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
    /// Nach dem Revocation bleibt die ResumeRequest GRANTED und `active` fällt auf
    /// false.
    /// </summary>
    /// <remarks>
    /// Genau deshalb steht die Berechtigung nicht im Case: „wurde einmal
    /// erteilt" und „gilt gerade" sind zwei verschiedene Aussagen.
    /// </remarks>
    [Fact]
    public async Task Nach_dem_Widerruf_bleibt_GRANTED_und_active_faellt()
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

        // Der Statuscode gehört mit geprüft: eine Gegenprobe hat gezeigt, dass
        // ein Revocation, der 500 antwortet, hier sonst durchginge — die
        // Zurückrollung liefe still ins Leere und der State sähe richtig aus.
        widerrufen.StatusCode.Should().Be(HttpStatusCode.OK);

        var nachher = (await Json(await ihr.GetAsync("/market/me/requests")))
            .EnumerateArray().Single();
        nachher.GetProperty("status").GetString().Should().Be("GRANTED");
        nachher.GetProperty("active").GetBoolean().Should().BeFalse();

        (await AlsFirma(company).GetAsync($"/market/{anna}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Das anfragende Unternehmen bekommt kein `active`.
    /// </summary>
    /// <remarks>
    /// Es hat die Antwort schon in Form des Status, den es sieht oder nicht
    /// sieht — und ein Feld hier ließe sich abfragen, ohne je einen MarketStatus
    /// zu lesen.
    /// </remarks>
    [Fact]
    public async Task Das_Unternehmen_sieht_kein_active()
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
    /// Auch abgelehnte bleiben für das Unternehmen sichtbar — sonst sähen
    /// „abgelehnt" und „nie gefragt" gleich aus.
    /// </summary>
    [Fact]
    public async Task Abgelehnte_Anfragen_bleiben_sichtbar()
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

    /// <summary>Die ResumeRequest hinterlässt ihre Absicht in der Outbox.</summary>
    [Fact]
    public async Task Die_Anfrage_hinterlaesst_die_Absicht_in_der_Outbox()
    {
        var anna = Guid.CreateVersion7();
        _ledger.ProfilFrei.Add(anna);

        await AlsFirma(Guid.CreateVersion7()).PostAsync($"/market/{anna}/requests", null);

        await using var context = postgres.Kontext();
        var vermerke = await context.Set<OutboxRow>()
            .Where(row => row.UserId == anna)
            .ToListAsync();

        vermerke.Should().ContainSingle().Which.Kind.Should().Be("market_request");
    }

    /// <summary>
    /// Schweigt der Ledger, entsteht keine ResumeRequest — und die Antwort ist 503.
    /// </summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_laesst_keine_Anfrage_zurueck()
    {
        var anna = Guid.CreateVersion7();
        _ledger.Schweigt = true;

        var response = await AlsFirma(Guid.CreateVersion7())
            .PostAsync($"/market/{anna}/requests", null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Kontext();
        (await context.Anfragen.AnyAsync(row => row.SubjectId == anna))
            .Should().BeFalse();
    }

    /// <summary>Ein MarketStatus liest sich nur mit einem Unternehmen.</summary>
    [Fact]
    public async Task Eine_Privatperson_liest_keinen_fremden_Marktstatus()
    {
        var response = await AlsPerson(Guid.CreateVersion7())
            .GetAsync($"/market/{Guid.CreateVersion7()}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung gibt es nichts.</summary>
    [Fact]
    public async Task Ohne_Token_ist_es_401()
    {
        (await _dienst.CreateClient().GetAsync("/market/me"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
