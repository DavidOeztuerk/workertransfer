using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Applications.Tests;

/// <summary>Bewerben, zurückziehen, bewegen — und was der Ledger dabei sieht.</summary>
[Collection(PostgresCollection.Name)]
public class BewerbungsreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeledger _ledger = new();
    private readonly Probestellen _stellen = new();
    private readonly Probemitglieder _mitglieder = new();
    private readonly ProbeKiZugang _ki = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:applications", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(
                    ServiceDescriptor.Scoped<IConsentWriter>(_ => _ledger));
                services.Replace(ServiceDescriptor.Scoped<IJobLookup>(_ => _stellen));
                services.Replace(ServiceDescriptor.Scoped<IDelivery, Probezustellung>());
                services.Replace(ServiceDescriptor.Scoped<ICompanyMemberLookup>(_ => _mitglieder));
                services.Replace(ServiceDescriptor.Scoped<IKiZugangAbfrage>(_ => _ki));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid subject, Guid company) => Mit(Tokenform.Firma(subject, company));

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

    private static Task<HttpResponseMessage> Bewirb(
        HttpClient browser, Guid job, bool resume = false, bool portfolio = false) =>
        browser.PostAsJsonAsync("/applications", new
        {
            job_id = job,
            message = "Ich baue gern verteilte Systeme.",
            shares_resume = resume,
            shares_portfolio = portfolio
        });

    /// <summary>
    /// Die Release entsteht im Ledger, nicht in dieser Datenbank — und sie
    /// nennt den Empfänger.
    /// </summary>
    [Fact]
    public async Task Bewerben_erteilt_die_Freigaben_fuer_genau_dieses_Unternehmen()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);

        _mitglieder.Fuer(new TenantId(company), new SubjectId(Guid.CreateVersion7()));

        var response = await Bewirb(AlsPerson(subject), job, resume: true);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await Json(response);
        body.GetProperty("status").GetString().Should().Be("submitted");
        body.GetProperty("tenant_id").GetGuid().Should().Be(company);
        body.GetProperty("shares_resume").GetBoolean().Should().BeTrue();

        _ledger.Erteilt.Should().Equal(
            $"profile.visibility:tenant:{company}",
            $"resume.visibility:tenant:{company}");
    }

    /// <summary>
    /// Eine eingegangene JobApplication erzeugt je Mitglied genau eine Outbox-Zeile.
    /// Die Zeile trägt keinen Inhalt — nur Kennung und Art (ADR-0025).
    /// </summary>
    [Fact]
    public async Task Bewerben_hinterlaesst_je_Mitglied_eine_inhaltsfreie_Absicht()
    {
        var company = Guid.CreateVersion7();
        var anna = new SubjectId(Guid.CreateVersion7());
        var bea = new SubjectId(Guid.CreateVersion7());
        var job = _stellen.Oeffentlich(company);
        _mitglieder.Fuer(new TenantId(company), anna, bea);

        var response = await Bewirb(AlsPerson(Guid.CreateVersion7()), job);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        await using var context = postgres.Kontext();
        var vermerke = await context.Set<OutboxRow>()
            .Where(z => z.UserId == anna.Value || z.UserId == bea.Value)
            .ToListAsync();

        vermerke.Should().HaveCount(2);
        vermerke.Select(z => z.UserId).Should().BeEquivalentTo([anna.Value, bea.Value]);
        vermerke.Should().OnlyContain(z => z.Kind == "application_received");
        vermerke.Should().OnlyContain(z => z.LastError == string.Empty);
    }

    /// <summary>
    /// Schweigt identity-service, darf keine JobApplication entstehen: sonst ginge
    /// sie hinaus, ohne dass jemand im Unternehmen davon erfährt.
    /// </summary>
    [Fact]
    public async Task Ein_schweigendes_Identity_laesst_keine_Bewerbung_zurueck()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        _mitglieder.Schweigt = true;

        var response = await Bewirb(AlsPerson(subject), job);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Kontext();
        (await context.Applications.AnyAsync(row => row.SubjectId == subject))
            .Should().BeFalse();
        _ledger.Erteilt.Should().BeEmpty();
    }

    /// <summary>Die Mappe hängt an der einen Kennung — fremd und fehlend sind 404.</summary>
    [Fact]
    public async Task Eine_Bewerbung_liest_nur_wer_sie_sehen_darf()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var id = (await Json(await Bewirb(AlsPerson(subject), job))).GetProperty("id").GetGuid();

        (await AlsPerson(subject).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AlsFirma(Guid.CreateVersion7(), company).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await AlsPerson(Guid.CreateVersion7()).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7()).GetAsync($"/applications/{id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Das Profile ist immer dabei — eine JobApplication ohne jede Angabe zur Person
    /// ist keine. Portfolio und Resume nur, wenn sie mitgeschickt werden.
    /// </summary>
    [Fact]
    public async Task Ohne_Anhaenge_geht_nur_das_Profil_in_den_Ledger()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);

        await Bewirb(AlsPerson(Guid.CreateVersion7()), job);

        _ledger.Erteilt.Should().Equal($"profile.visibility:tenant:{company}");
    }

    /// <summary>
    /// Schweigt der Ledger, darf keine JobApplication entstehen: das Unternehmen
    /// sähe einen Case, dessen Daten es nicht lesen darf.
    /// </summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_laesst_keine_Bewerbung_zurueck()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        _ledger.Schweigt = true;

        var response = await Bewirb(AlsPerson(subject), job);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await using var context = postgres.Kontext();
        (await context.Applications.AnyAsync(row => row.SubjectId == subject))
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

        var response = await Bewirb(AlsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Entwurf, geschlossen und nicht vorhanden bleiben ununterscheidbar.</summary>
    [Fact]
    public async Task Eine_Stelle_die_es_oeffentlich_nicht_gibt_ist_404()
    {
        var response = await Bewirb(AlsPerson(Guid.CreateVersion7()), Guid.CreateVersion7());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Der Rückzug widerruft alle vier — auch, was gar nicht erteilt wurde.
    /// </summary>
    /// <remarks>
    /// Das schließt die Lücke, die ein geglückter Ledger-Aufruf mit
    /// fehlgeschlagenem Commit hinterlassen hätte.
    /// </remarks>
    [Fact]
    public async Task Zurueckziehen_widerruft_bedingungslos_alle_vier()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var browser = AlsPerson(subject);

        var id = (await Json(await Bewirb(browser, job))).GetProperty("id").GetGuid();

        var response = await browser.PostAsync($"/applications/{id}/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("status").GetString().Should().Be("withdrawn");

        _ledger.Widerrufen.Should().Equal(
            $"profile.visibility:tenant:{company}",
            $"resume.visibility:tenant:{company}",
            $"portfolio.visibility:tenant:{company}",
            // Die vierte kam mit der Bewerbungsmappe dazu (ADR-0035). Sie ist
            // eigenstaendig und nicht unter `resume` mitgefuehrt: der Werdegang
            // ist selbst geschriebener Text, ein Reference ein Dokument Dritter
            // mit Namen und Noten — wer das eine zeigen will und das andere
            // nicht, muss das koennen.
            $"documents.visibility:tenant:{company}");
    }

    /// <summary>Eine fremde JobApplication ist von außen wie keine.</summary>
    [Fact]
    public async Task Eine_fremde_Bewerbung_zurueckzuziehen_ist_404_und_nicht_403()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AlsPerson(Guid.CreateVersion7())
            .PostAsync($"/applications/{id}/withdraw", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Nach einem Rückzug ist eine neue Entscheidung möglich — nach einer
    /// Ablehnung nicht.
    /// </summary>
    [Fact]
    public async Task Nach_dem_Rueckzug_geht_es_wieder_nach_der_Absage_nicht()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var person = AlsPerson(subject);
        var unternehmen = AlsFirma(Guid.CreateVersion7(), company);

        var id = (await Json(await Bewirb(person, job))).GetProperty("id").GetGuid();
        await person.PostAsync($"/applications/{id}/withdraw", null);

        var wieder = await Bewirb(person, job);

        wieder.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(wieder)).GetProperty("id").GetGuid().Should().Be(id);

        (await unternehmen.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "rejected" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var nachfassen = await Bewirb(person, job);

        nachfassen.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Der Zug des Unternehmens wird gemeldet — und der Vermerk committet mit
    /// ihm (ADR-0025).
    /// </summary>
    [Fact]
    public async Task Bewegen_hinterlaesst_die_Absicht_in_der_Outbox()
    {
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);

        var id = (await Json(await Bewirb(AlsPerson(subject), job))).GetProperty("id").GetGuid();

        var response = await AlsFirma(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("status").GetString().Should().Be("reviewing");

        await using var context = postgres.Kontext();
        var vermerke = await context.Set<OutboxRow>()
            .Where(row => row.UserId == subject)
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
        var company = Guid.CreateVersion7();
        var subject = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var browser = AlsPerson(subject);

        var id = (await Json(await Bewirb(browser, job))).GetProperty("id").GetGuid();
        await browser.PostAsync($"/applications/{id}/withdraw", null);

        await using var context = postgres.Kontext();
        (await context.Set<OutboxRow>().AnyAsync(row => row.UserId == subject))
            .Should().BeFalse();
    }

    /// <summary>
    /// Ein Unternehmen zieht keine JobApplication zurück — das ist die Handlung der
    /// Person, und sie hier zuzulassen hieße, sie in ihrem Namen zu tun.
    /// </summary>
    [Fact]
    public async Task Ein_Unternehmen_kann_nicht_in_ihrem_Namen_zurueckziehen()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AlsFirma(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "withdrawn" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Ein Standwort, das es nicht gibt, ist eine Eingabe und kein Absturz.</summary>
    [Fact]
    public async Task Ein_erfundener_Stand_ist_422()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AlsFirma(Guid.CreateVersion7(), company)
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "eingeladen" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>Eine fremde Firma sieht 404, nicht 403.</summary>
    [Fact]
    public async Task Eine_fremde_Firma_kann_eine_Bewerbung_nicht_bewegen()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();

        var response = await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .PostAsJsonAsync($"/applications/{id}/status", new { status = "reviewing" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Eine fremde Job liefert nichts, statt zu verraten, dass es sie gibt.</summary>
    [Fact]
    public async Task Die_Liste_zur_Stelle_zeigt_nur_die_eigene()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        await Bewirb(AlsPerson(Guid.CreateVersion7()), job);

        var eigene = await AlsFirma(Guid.CreateVersion7(), company)
            .GetAsync($"/jobs/{job}/applications");
        var fremde = await AlsFirma(Guid.CreateVersion7(), Guid.CreateVersion7())
            .GetAsync($"/jobs/{job}/applications");

        (await Json(eigene)).GetArrayLength().Should().Be(1);
        (await Json(fremde)).GetArrayLength().Should().Be(0);
    }

    /// <summary>Applications liest, wer für ein Unternehmen handelt.</summary>
    [Fact]
    public async Task Eine_Privatperson_liest_keine_Bewerbungsliste()
    {
        var response = await AlsPerson(Guid.CreateVersion7())
            .GetAsync($"/jobs/{Guid.CreateVersion7()}/applications");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Die eigene Liste, und nur die eigene.</summary>
    [Fact]
    public async Task Die_eigene_Liste_zeigt_die_eigenen_Bewerbungen()
    {
        var subject = Guid.CreateVersion7();
        var company = Guid.CreateVersion7();
        await Bewirb(AlsPerson(subject), _stellen.Oeffentlich(company));
        await Bewirb(AlsPerson(subject), _stellen.Oeffentlich(company));
        await Bewirb(AlsPerson(Guid.CreateVersion7()), _stellen.Oeffentlich(company));

        var meine = await Json(await AlsPerson(subject).GetAsync("/applications/me"));

        meine.GetArrayLength().Should().Be(2);
        meine.EnumerateArray()
            .Should().OnlyContain(entry => entry.GetProperty("subject_id").GetGuid() == subject);
    }

    /// <summary>
    /// Zahlen über die eigenen Vorgänge: eine Bequemlichkeit, keine neue
    /// Auskunft — und nichts, was Quellen zusammenführt.
    /// </summary>
    [Fact]
    public async Task Die_Zahlen_zaehlen_nur_die_eigenen_Vorgaenge()
    {
        var company = Guid.CreateVersion7();
        var job = _stellen.Oeffentlich(company);
        var unternehmen = AlsFirma(Guid.CreateVersion7(), company);

        var id = (await Json(await Bewirb(AlsPerson(Guid.CreateVersion7()), job)))
            .GetProperty("id").GetGuid();
        await Bewirb(AlsPerson(Guid.CreateVersion7()), job);
        await unternehmen.PostAsJsonAsync(
            $"/applications/{id}/status", new { status = "hired" });

        // Eine fremde Firma mit eigener JobApplication darf die Zahlen nicht bewegen.
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

        zahlen.EnumerateObject().Select(field => field.Name).Should().BeEquivalentTo(
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
        var response = await AlsPerson(Guid.CreateVersion7())
            .GetAsync("/companies/me/application-stats");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Ohne Anmeldung geht nichts.</summary>
    [Fact]
    public async Task Ohne_Token_ist_es_401()
    {
        var response = await _dienst.CreateClient().GetAsync("/applications/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Ohne Anbieter bleibt der Entwurf nicht auf „wird geschrieben" stehen.
    /// </summary>
    /// <remarks>
    /// 503 sagt der Oberfläche, dass niemand eingerichtet ist. Der State muss
    /// trotzdem <c>failed</c> werden, sonst darf die Person den Text nicht
    /// selbst setzen — <c>Aendere_selbst</c> ist aus <c>generating</c> verboten.
    /// </remarks>
    [Fact]
    public async Task Ohne_Anbieter_wird_der_Entwurf_fehlgeschlagen_und_nicht_erzeugt()
    {
        var job = _stellen.Oeffentlich(Guid.CreateVersion7());
        var browser = AlsPerson(Guid.CreateVersion7());

        var angelegt = await browser.PostAsJsonAsync(
            "/applications/drafts", new { job_ids = new[] { job } });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        var draft = (await Json(angelegt))[0];
        var id = draft.GetProperty("id").GetGuid();
        draft.GetProperty("status").GetString().Should().Be("generating");

        var schreiben = await browser.PostAsync($"/applications/drafts/{id}/write", null);

        schreiben.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var danach = await Json(await browser.GetAsync($"/applications/drafts/{id}"));

        danach.GetProperty("status").GetString().Should().Be("failed");
        danach.GetProperty("body").GetString().Should().BeEmpty();
        danach.GetProperty("error").GetString().Should().Contain("kein Entwurfsanbieter");
    }
}
