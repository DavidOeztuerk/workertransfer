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
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Bewerben, zurückziehen, bewegen — und was der Ledger dabei sieht.</summary>
[Collection(PostgresCollection.Name)]
public class BewerbungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();
    private readonly Probestellen _stellen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(
                    ServiceDescriptor.Scoped<IEinwilligungsschreiber>(_ => _ledger));
                dienste.Replace(ServiceDescriptor.Scoped<IStellenauskunft>(_ => _stellen));
                dienste.Replace(ServiceDescriptor.Scoped<IZustellung, Probezustellung>());
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid wer, Guid firma) => Mit(Tokenform.Firma(wer, firma));

    private HttpClient AlsPerson(Guid wer) => Mit(Tokenform.Person(wer));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Bewirb(
        HttpClient browser, Guid stelle, bool lebenslauf = false, bool portfolio = false) =>
        browser.PostAsJsonAsync("/applications", new
        {
            job_id = stelle,
            message = "Ich baue gern verteilte Systeme.",
            shares_resume = lebenslauf,
            shares_portfolio = portfolio
        });

    /// <summary>
    /// Die Freigabe entsteht im Ledger, nicht in dieser Datenbank — und sie
    /// nennt den Empfänger.
    /// </summary>
    [Fact]
    public async Task Bewerben_erteilt_die_Freigaben_fuer_genau_dieses_Unternehmen()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);

        var antwort = await Bewirb(AlsPerson(wer), stelle, lebenslauf: true);

        antwort.StatusCode.Should().Be(HttpStatusCode.Created);

        var rumpf = await Json(antwort);
        rumpf.GetProperty("status").GetString().Should().Be("submitted");
        rumpf.GetProperty("tenant_id").GetGuid().Should().Be(firma);
        rumpf.GetProperty("shares_resume").GetBoolean().Should().BeTrue();

        _ledger.Erteilt.Should().Equal(
            $"profile.visibility:tenant:{firma}",
            $"resume.visibility:tenant:{firma}");
    }

    /// <summary>
    /// Das Profil ist immer dabei — eine Bewerbung ohne jede Angabe zur Person
    /// ist keine. Portfolio und Lebenslauf nur, wenn sie mitgeschickt werden.
    /// </summary>
    [Fact]
    public async Task Ohne_Anhaenge_geht_nur_das_Profil_in_den_Ledger()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);

        await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle);

        _ledger.Erteilt.Should().Equal($"profile.visibility:tenant:{firma}");
    }

    /// <summary>
    /// Schweigt der Ledger, darf keine Bewerbung entstehen: das Unternehmen
    /// sähe einen Vorgang, dessen Daten es nicht lesen darf.
    /// </summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_laesst_keine_Bewerbung_zurueck()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        _ledger.Schweigt = true;

        var antwort = await Bewirb(AlsPerson(wer), stelle);

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var kontext = postgres.Kontext();
        (await kontext.Bewerbungen.AnyAsync(zeile => zeile.SubjectId == wer))
            .Should().BeFalse();
    }

    /// <summary>
    /// Ein schweigender Jobs-Dienst ist kein „gibt es nicht": die Person bekäme
    /// sonst eine Absage, die niemand ausgesprochen hat.
    /// </summary>
    [Fact]
    public async Task Ein_schweigender_Jobs_Dienst_wird_503_und_nicht_404()
    {
        _stellen.Schweigt = true;

        var antwort = await Bewirb(AlsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Entwurf, geschlossen und nicht vorhanden bleiben ununterscheidbar.</summary>
    [Fact]
    public async Task Eine_Stelle_die_es_oeffentlich_nicht_gibt_ist_404()
    {
        var antwort = await Bewirb(AlsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Der Rückzug widerruft alle drei — auch, was gar nicht erteilt wurde.
    /// </summary>
    /// <remarks>
    /// Das schließt die Lücke, die ein geglückter Ledger-Aufruf mit
    /// fehlgeschlagenem Commit hinterlassen hätte.
    /// </remarks>
    [Fact]
    public async Task Zurueckziehen_widerruft_bedingungslos_alle_drei()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var browser = AlsPerson(wer);

        var id = (await Json(await Bewirb(browser, stelle))).GetProperty("id").GetGuid();

        var antwort = await browser.PostAsync($"/applications/{id}/withdraw", null);

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("status").GetString().Should().Be("withdrawn");

        _ledger.Widerrufen.Should().Equal(
            $"profile.visibility:tenant:{firma}",
            $"resume.visibility:tenant:{firma}",
            $"portfolio.visibility:tenant:{firma}");
    }

    /// <summary>Eine fremde Bewerbung ist von außen wie keine.</summary>
    [Fact]
    public async Task Eine_fremde_Bewerbung_zurueckzuziehen_ist_404_und_nicht_403()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle)))
            .GetProperty("id").GetGuid();

        var antwort = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/applications/{id}/withdraw", null);

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Nach einem Rückzug ist eine neue Entscheidung möglich — nach einer
    /// Ablehnung nicht.
    /// </summary>
    [Fact]
    public async Task Nach_dem_Rueckzug_geht_es_wieder_nach_der_Absage_nicht()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var person = AlsPerson(wer);
        var unternehmen = AlsFirma(Guid.CreateVersion7(), firma);

        var id = (await Json(await Bewirb(person, stelle))).GetProperty("id").GetGuid();
        await person.PostAsync($"/applications/{id}/withdraw", null);

        var wieder = await Bewirb(person, stelle);

        wieder.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(wieder)).GetProperty("id").GetGuid().Should().Be(id);

        (await unternehmen.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "rejected" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var nachfassen = await Bewirb(person, stelle);

        nachfassen.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Der Zug des Unternehmens wird gemeldet — und der Vermerk committet mit
    /// ihm (ADR-0025).
    /// </summary>
    [Fact]
    public async Task Bewegen_hinterlaesst_die_Absicht_in_der_Outbox()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);

        var id = (await Json(await Bewirb(AlsPerson(wer), stelle))).GetProperty("id").GetGuid();

        var antwort = await AlsFirma(Guid.CreateVersion7(), firma)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(antwort)).GetProperty("status").GetString().Should().Be("reviewing");

        await using var kontext = postgres.Kontext();
        var vermerke = await kontext.Set<OutboxZeile>()
            .Where(zeile => zeile.UserId == wer)
            .ToListAsync();

        vermerke.Should().ContainSingle()
            .Which.Kind.Should().Be("application_update");
    }

    /// <summary>
    /// Der Rückzug durch die Person wird NICHT gemeldet: sie weiß, was sie
    /// getan hat.
    /// </summary>
    [Fact]
    public async Task Der_Rueckzug_der_Person_wird_nicht_gemeldet()
    {
        var firma = Guid.CreateVersion7();
        var wer = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var browser = AlsPerson(wer);

        var id = (await Json(await Bewirb(browser, stelle))).GetProperty("id").GetGuid();
        await browser.PostAsync($"/applications/{id}/withdraw", null);

        await using var kontext = postgres.Kontext();
        (await kontext.Set<OutboxZeile>().AnyAsync(zeile => zeile.UserId == wer))
            .Should().BeFalse();
    }

    /// <summary>
    /// Ein Unternehmen zieht keine Bewerbung zurück — das ist die Handlung der
    /// Person, und sie hier zuzulassen hieße, sie in ihrem Namen zu tun.
    /// </summary>
    [Fact]
    public async Task Ein_Unternehmen_kann_nicht_in_ihrem_Namen_zurueckziehen()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle)))
            .GetProperty("id").GetGuid();

        var antwort = await AlsFirma(Guid.CreateVersion7(), firma)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "withdrawn" });

        antwort.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Ein Standwort, das es nicht gibt, ist eine Eingabe und kein Absturz.</summary>
    [Fact]
    public async Task Ein_erfundener_Stand_ist_422()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle)))
            .GetProperty("id").GetGuid();

        var antwort = await AlsFirma(Guid.CreateVersion7(), firma)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "eingeladen" });

        antwort.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Eine fremde Firma sieht 404, nicht 403.</summary>
    [Fact]
    public async Task Eine_fremde_Firma_kann_eine_Bewerbung_nicht_bewegen()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle)))
            .GetProperty("id").GetGuid();

        var antwort = await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Eine fremde Stelle liefert nichts, statt zu verraten, dass es sie gibt.</summary>
    [Fact]
    public async Task Die_Liste_zur_Stelle_zeigt_nur_die_eigene()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle);

        var eigene = await AlsFirma(Guid.CreateVersion7(), firma)
            .GetAsync($"/jobs/{stelle}/applications");
        var fremde = await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .GetAsync($"/jobs/{stelle}/applications");

        (await Json(eigene)).GetArrayLength().Should().Be(1);
        (await Json(fremde)).GetArrayLength().Should().Be(0);
    }

    /// <summary>Bewerbungen liest, wer für ein Unternehmen handelt.</summary>
    [Fact]
    public async Task Eine_Privatperson_liest_keine_Bewerbungsliste()
    {
        var antwort = await AlsPerson(Guid.CreateVersion7())
            .GetAsync($"/jobs/{Guid.CreateVersion7()}/applications");

        antwort.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Die eigene Liste, und nur die eigene.</summary>
    [Fact]
    public async Task Die_eigene_Liste_zeigt_die_eigenen_Bewerbungen()
    {
        var wer = Guid.CreateVersion7();
        var firma = Guid.CreateVersion7();
        await Bewirb(AlsPerson(wer), _stellen.Oeffentlich(firma));
        await Bewirb(AlsPerson(wer), _stellen.Oeffentlich(firma));
        await Bewirb(AlsPerson(Guid.CreateVersion7()), _stellen.Oeffentlich(firma));

        var meine = await Json(await AlsPerson(wer).GetAsync("/applications/me"));

        meine.GetArrayLength().Should().Be(2);
        meine.EnumerateArray()
            .Should().OnlyContain(eintrag => eintrag.GetProperty("subject_id").GetGuid() == wer);
    }

    /// <summary>
    /// Zahlen über die eigenen Vorgänge: eine Bequemlichkeit, keine neue
    /// Auskunft — und nichts, was Quellen zusammenführt.
    /// </summary>
    [Fact]
    public async Task Die_Zahlen_zaehlen_nur_die_eigenen_Vorgaenge()
    {
        var firma = Guid.CreateVersion7();
        var stelle = _stellen.Oeffentlich(firma);
        var unternehmen = AlsFirma(Guid.CreateVersion7(), firma);

        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle)))
            .GetProperty("id").GetGuid();
        await Bewirb(AlsPerson(Guid.CreateVersion7()), stelle);
        await unternehmen.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "hired" });

        // Eine fremde Firma mit eigener Bewerbung darf die Zahlen nicht bewegen.
        await Bewirb(
            AlsPerson(Guid.CreateVersion7()),
            _stellen.Oeffentlich(Guid.CreateVersion7()));

        var zahlen = await Json(await unternehmen.GetAsync("/companies/me/application-stats"));

        zahlen.GetProperty("total").GetInt32().Should().Be(2);
        zahlen.GetProperty("by_status").GetProperty("hired").GetInt32().Should().Be(1);
        zahlen.GetProperty("by_status").GetProperty("submitted").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// Die Antwort trägt genau zwei Felder — was es nicht gibt, kann nicht
    /// herausgehen.
    /// </summary>
    /// <remarks>
    /// ADR-0026 sagt zu: „Ein Test hält die Feldmenge der Antwort fest — was es
    /// nicht gibt, kann nicht herausgehen (dieselbe Strenge wie bei
    /// <c>DraftContext</c>, ADR-0024)." <b>Den Test gab es nicht.</b> Geprüft
    /// wurden drei Werte, und ein viertes Feld wäre still hinausgegangen.
    /// <para>
    /// Genau dagegen steht die Zusage: eine Auswertung wächst nicht durch eine
    /// Entscheidung, sondern durch ein „das können wir auch gleich mitgeben" —
    /// und der nächste Schritt wäre eine Zahl je Person, die ADR-0022 verbietet.
    /// Eine geschlossene Menge macht diesen Schritt sichtbar, statt ihn zu
    /// verhindern: wer sie erweitert, schreibt es hier hin.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Die_Zahlen_tragen_genau_zwei_Felder()
    {
        var unternehmen = AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7());

        var zahlen = await Json(
            await unternehmen.GetAsync("/companies/me/application-stats"));

        zahlen.EnumerateObject().Select(feld => feld.Name).Should().BeEquivalentTo(
            ["total", "by_status"],
            "ADR-0026 nennt diese Menge und keine groessere");
    }

    /// <summary>Ohne aktives Unternehmen gibt es keine Zahlen.</summary>
    /// <remarks>
    /// ADR-0026 nennt die 403 ausdrücklich. Geprüft war sie nur an
    /// <c>/jobs/{id}/applications</c> — ein anderer Endpunkt, dessen grüner Test
    /// über diesen nichts aussagt. Die Zahlen sind die Auswertung eines
    /// Unternehmens; wer für keines handelt, fragt nach fremden.
    /// </remarks>
    [Fact]
    public async Task Ohne_Unternehmen_gibt_es_keine_Zahlen()
    {
        var antwort = await AlsPerson(Guid.CreateVersion7())
            .GetAsync("/companies/me/application-stats");

        antwort.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung geht nichts.</summary>
    [Fact]
    public async Task Ohne_Token_ist_es_401()
    {
        var antwort = await _dienst.CreateClient().GetAsync("/applications/me");

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
