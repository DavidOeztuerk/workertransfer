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
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Hit;
using WorkerTransfer.Scout.Infrastructure.Persistence;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Tests;

/// <summary>Searches, ablegen, ansprechen — und gelöscht werden.</summary>
/// <remarks>
/// Die Reihe geht den ganzen Weg über den Draht, weil zwischen einem Handler
/// und dem, was ein Browser bekommt, noch die Einstellungen, die Verdrahtung
/// und die Feldnamen liegen. Ein Feld, das in camelCase gebunden wird, kommt
/// nie an — und jede Handlerreihe bliebe grün.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ScoutreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private static readonly Guid Firma = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Werber = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probesuche _suche = new();
    private readonly Probetor _tor = new();
    private readonly Probebelege _belege = new();
    private readonly Probeentwerfer _entwerfer = new();
    private readonly Probestellen _stellen = new();

    public async Task InitializeAsync()
    {
        // Die Reihen teilen sich EINE Datenbank, und xUnit gibt innerhalb einer
        // Sammlung keine Reihenfolge zu. Ohne dieses Leeren waere jede Zusage
        // ueber „genau eine Zeile" davon abhaengig, wer vorher lief — und das
        // faellt erst auf, wenn sich die Reihenfolge irgendwann aendert.
        await using (var vorlauf = Kontext())
        {
            await vorlauf.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE searches, outbox");
        }

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:scout", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IProfileSearch>(_ => _suche));
                services.Replace(ServiceDescriptor.Scoped<IConsentGate>(_ => _tor));
                services.Replace(ServiceDescriptor.Scoped<IEvidenceSource>(_ => _belege));
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _entwerfer));
                services.Replace(ServiceDescriptor.Scoped<IJobs>(_ => _stellen));
            });
        });
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------
    // Firmenzwang
    // ---------------------------------------------------------------------

    /// <summary>Ohne Token: 401. Als Person ohne Firma: 403.</summary>
    /// <remarks>
    /// Die zweite Hälfte ist die, die man vergisst. Auf einem TransferMarket ist
    /// ein Mensch ohne Firma der Normalfall — und 403 ist eine Aussage über den
    /// <em>Aufrufer</em>, die über die Gesuchten nichts verrät.
    /// </remarks>
    [Fact]
    public async Task Suchen_darf_nur_wer_fuer_eine_Firma_handelt()
    {
        var ohne = await _dienst.CreateClient().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        ohne.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var person = await AlsPerson().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        person.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var company = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        company.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------
    // Der Ledger
    // ---------------------------------------------------------------------

    /// <summary>Der Ledger wird bei JEDEM Aufruf gefragt.</summary>
    /// <remarks>
    /// ADR-0013: ein Revocation muss beim nächsten Lesen wirken. Ein
    /// Zwischenspeicher wäre hier kein Leistungsdetail, sondern ein Regelbruch —
    /// und er fiele erst auf, wenn jemand widerrufen hat und trotzdem gefunden
    /// wird.
    /// </remarks>
    [Fact]
    public async Task Jeder_Aufruf_fragt_den_Ledger_erneut()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject));
        _tor.Frei.Add((subject, Firma));

        var vorher = _tor.Fragen;

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (_tor.Fragen - vorher).Should().Be(2);
    }

    /// <summary>Ein schweigender Ledger ist weder ein Ja noch ein Nein: 503.</summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_gibt_503()
    {
        _suche.Bestand.Add(Profile(Guid.NewGuid()));
        _tor.Schweigt = true;

        var response = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(response)).GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Eine Antwort, die nicht zu den Fragen passt, wird nicht geraten.
    /// </summary>
    /// <remarks>
    /// Falsch zuzuordnen hiesse, das Profile der falschen Person zu zeigen. 503
    /// und nicht „so gut es geht".
    /// </remarks>
    [Fact]
    public async Task Eine_unpassende_Ledgerantwort_wird_nicht_geraten()
    {
        _suche.Bestand.Add(Profile(Guid.NewGuid()));
        _suche.Bestand.Add(Profile(Guid.NewGuid()));
        _tor.AntwortetMitLaenge = 1;

        var response = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Eine verschlossene Suchtuer gibt 503 — und ausdrücklich keine leere Liste.
    /// </summary>
    /// <remarks>
    /// <para><strong>Das ist der Test zu einem gemessenen Fehler.</strong> Die
    /// interne Suchtür von profile-service antwortet ohne das geteilte Geheimnis
    /// mit <c>404</c> statt <c>401</c>, damit sie sich nicht verrät. Der Adapter
    /// las das als „nicht gefunden" und gab eine leere Seite zurück; der
    /// Endpunkt antwortete <c>200</c> mit <c>items: []</c>, und die Oberfläche
    /// behauptete, es gebe niemanden.</para>
    ///
    /// <para>Am laufenden Stapel fielen daraufhin zwölf E2E-Reisen — während
    /// die Routenkarte grün blieb, weil sie Statuscodes prüft und keine Rümpfe.
    /// „Nicht eingerichtet" darf nicht aussehen wie „es gibt niemanden".</para>
    /// </remarks>
    [Fact]
    public async Task Eine_verschlossene_Suchtuer_gibt_503_und_keine_leere_Liste()
    {
        _suche.Verschlossen = true;

        var response = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(
            HttpStatusCode.ServiceUnavailable,
            "eine leere Trefferliste waere eine Aussage ueber Menschen, die aus "
            + "einer fehlenden Umgebungsvariablen stammt");
    }

    /// <summary>Eine schweigende Profilsuche gibt 503 — keine leere Liste.</summary>
    /// <remarks>
    /// Eine leere Trefferliste sähe aus wie „es gibt niemanden", und das wäre
    /// eine Aussage über Menschen, die aus unserem Ausfall stammt.
    /// </remarks>
    [Fact]
    public async Task Eine_schweigende_Profilsuche_gibt_503()
    {
        _suche.Schweigt = true;

        var response = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>Wer nicht freigegeben hat, taucht nicht auf — und die Seite bleibt kurz.</summary>
    /// <remarks>
    /// Nicht nachgeladen (ADR-0020 §4), keine Gesamtzahl (ADR-0026): beides
    /// verriete, wie viele Profile es gibt, die niemand freigegeben hat.
    /// </remarks>
    [Fact]
    public async Task Die_Seite_zeigt_nur_Freigegebene_und_nennt_keine_Gesamtzahl()
    {
        var frei = Guid.NewGuid();
        var verborgen = Guid.NewGuid();

        _suche.Bestand.Add(Profile(frei));
        _suche.Bestand.Add(Profile(verborgen));
        _tor.Frei.Add((frei, Firma));

        var response = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        var body = await Json(response);

        body.GetProperty("items").EnumerateArray()
            .Select(entry => entry.GetProperty("subject_id").GetGuid())
            .Should().Equal(frei);

        body.TryGetProperty("total", out _).Should().BeFalse();
        body.TryGetProperty("count", out _).Should().BeFalse();
        body.EnumerateObject().Select(field => field.Name)
            .Should().BeEquivalentTo("items", "next");
    }

    // ---------------------------------------------------------------------
    // „Dein Profile wurde entdeckt"
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wer in einer Search auftaucht, bekommt einen Vermerk im Postausgang —
    /// und der nennt kein Unternehmen.
    /// </summary>
    /// <remarks>
    /// <para>ADR-0033: eine Nachricht statt eines Protokolls „wer hat wen
    /// angesehen". Der Postausgang bleibt inhaltsfrei (ADR-0025) — Kennung und
    /// Art, sonst nichts. Dass der Mandant <em>nicht</em> mitreist, ist hier
    /// nachgemessen und nicht behauptet: die Zeile hat gar keine Spalte dafür.</para>
    ///
    /// <para>Und wer nicht freigegeben hat, bekommt keinen Vermerk: er ist auch
    /// nicht entdeckt worden.</para>
    /// </remarks>
    [Fact]
    public async Task Wer_gefunden_wird_erfaehrt_es_ohne_zu_erfahren_von_wem()
    {
        var frei = Guid.NewGuid();
        var verborgen = Guid.NewGuid();

        _suche.Bestand.Add(Profile(frei));
        _suche.Bestand.Add(Profile(verborgen));
        _tor.Frei.Add((frei, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        var vermerke = await Postausgang();

        vermerke.Where(row => row.UserId == frei)
            .Should().ContainSingle()
            .Which.Kind.Should().Be("profile_discovered");

        vermerke.Should().NotContain(row => row.UserId == verborgen);

        // Die Zeile traegt Kennung und Art. Der Mandant steht nirgends darin —
        // „ein Unternehmen" genuegt, und wer sucht, ist eine Aussage ueber das
        // Unternehmen.
        var zeilenfelder = typeof(OutboxRow).GetProperties().Select(e => e.Name);
        zeilenfelder.Should().NotContain("TenantId");
    }

    /// <summary>Derselbe Mensch bekommt je Seite höchstens einen Vermerk.</summary>
    [Fact]
    public async Task Derselbe_Mensch_bekommt_je_Seite_einen_Vermerk()
    {
        var subject = Guid.NewGuid();

        // Zweimal derselbe Mensch in einer Seite — das kann profile-service
        // nicht liefern, aber der Befehl soll es trotzdem aushalten.
        _suche.Bestand.Add(Profile(subject));
        _suche.Bestand.Add(Profile(subject));
        _tor.Frei.Add((subject, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (await Postausgang()).Count(row => row.UserId == subject).Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Die Availability (ADR-0041)
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wer weiter weg wohnt, BLEIBT in der Liste — und trägt ein Kreuz.
    /// </summary>
    /// <remarks>
    /// <para>Die Kernauflage von ADR-0041, und sie ist eine Aussage über die
    /// <em>Länge</em> der Liste: die Job filtert nicht. Beide Menschen kommen
    /// zurück, einer mit Haken, einer mit Kreuz. Das Unternehmen entscheidet,
    /// nicht die Search — und es sieht, warum.</para>
    ///
    /// <para>Nebenbei die zweite Zusage: neben dem Häkchen steht keine
    /// Kilometerzahl, sondern stehen die beiden <em>Aussagen</em>.</para>
    /// </remarks>
    [Fact]
    public async Task Wer_weiter_weg_wohnt_bleibt_in_der_Liste()
    {
        var nah = Guid.NewGuid();
        var fern = Guid.NewGuid();
        var job = Guid.NewGuid();

        _suche.Bestand.Add(Profile(nah) with { Ort = "Potsdam", Pendelstufe = Pendelstufe.Bis50 });
        _suche.Bestand.Add(Profile(fern) with { Ort = "München", Pendelstufe = Pendelstufe.Bis10 });
        _tor.Frei.Add((nah, Firma));
        _tor.Frei.Add((fern, Firma));
        _stellen.Bestand[job] = new JobStatement("Berlin", "", Anwesenheit.VorOrt);

        var body = await Json(await AlsFirma().GetAsync(
            new Uri($"/scout/candidates?stelle={job}", UriKind.Relative)));

        var entries = body.GetProperty("items").EnumerateArray()
            .ToDictionary(e => e.GetProperty("subject_id").GetGuid());

        entries.Should().HaveCount(2, "die Stelle filtert nicht, sie erklaert");
        entries[nah].GetProperty("reach").GetString().Should().Be("reachable");
        entries[fern].GetProperty("reach").GetString().Should().Be("further");

        // Die Aussagen stehen daneben, die Entfernung nicht.
        entries[fern].GetProperty("reach_commute").GetString().Should().Be("bis_10");
        entries[fern].GetProperty("reach_attendance").GetString().Should().Be("vor_ort");

        var text = body.ToString();
        foreach (var verboten in new[] { "km", "distance", "entfernung", "505" })
        {
            text.Should().NotContain(verboten, "keine Kilometerzahl auf dem Draht");
        }
    }

    /// <summary>Ohne Job trägt jeder Hit einen Strich — und keinen Fehler.</summary>
    [Fact]
    public async Task Ohne_Stelle_traegt_jeder_Treffer_einen_Strich()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject) with { Pendelstufe = Pendelstufe.Bis10 });
        _tor.Frei.Add((subject, Firma));

        var body = await Json(await AlsFirma().GetAsync(
            new Uri("/scout/candidates", UriKind.Relative)));

        body.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("reach").GetString().Should().Be("unsaid");

        _stellen.Fragen.Should().Be(0, "ohne Stelle wird jobs-service gar nicht gefragt");
    }

    /// <summary>
    /// Eine schweigende Job kostet das Häkchen, nicht die HitPage.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zum Ledger und zur Profilsuche, und er ist bewusst: dort
    /// heisst Schweigen 503, weil eine leere Liste eine Aussage über Menschen
    /// wäre. Hier fehlt nur eine Auskunft ÜBER die Liste — die Hit selbst
    /// sind vollständig, und ein 503 nähme sie alle weg (ADR-0041).
    /// </remarks>
    [Fact]
    public async Task Eine_schweigende_Stelle_kostet_nur_das_Haekchen()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject) with { Pendelstufe = Pendelstufe.Bis50 });
        _tor.Frei.Add((subject, Firma));
        _stellen.Schweigt = true;

        var response = await AlsFirma().GetAsync(
            new Uri($"/scout/candidates?stelle={Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("reach").GetString().Should().Be("unsaid");
    }

    /// <summary>Die Job wird EINMAL je Seite gefragt, nicht je Zeile.</summary>
    /// <remarks>
    /// Sie ist für alle Hit dieselbe. Einmal je Zeile zu fragen wäre
    /// derselbe Fehler, den die Sammelfrage an den Ledger behoben hat — nur
    /// gegenüber einem anderen Dienst.
    /// </remarks>
    [Fact]
    public async Task Die_Stelle_wird_einmal_je_Seite_gefragt()
    {
        var job = Guid.NewGuid();
        _stellen.Bestand[job] = new JobStatement("Berlin", "", Anwesenheit.VorOrt);

        foreach (var _ in Enumerable.Range(0, 5))
        {
            var subject = Guid.NewGuid();
            _suche.Bestand.Add(Profile(subject));
            _tor.Frei.Add((subject, Firma));
        }

        await AlsFirma().GetAsync(
            new Uri($"/scout/candidates?stelle={job}", UriKind.Relative));

        _stellen.Fragen.Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Die gespeicherte ResumeRequest
    // ---------------------------------------------------------------------

    /// <summary>Gespeichert wird die ResumeRequest — und nachweislich kein Ergebnis.</summary>
    /// <remarks>
    /// Die Gegenprobe steckt in der zweiten Hälfte: es wird gesucht, es gibt
    /// einen Hit, die Search wird abgelegt — und in der abgelegten Search
    /// steht der Hit nicht. Ein gespeichertes Ergebnis über Menschen
    /// veraltete gegen einen Revocation (ADR-0036 Entscheidung 4).
    /// </remarks>
    [Fact]
    public async Task Gespeichert_wird_die_Anfrage_nie_das_Ergebnis()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject, ["Go"]));
        _tor.Frei.Add((subject, Firma));

        var found = await AlsFirma().GetAsync(
            new Uri("/scout/candidates?skill=Go", UriKind.Relative));
        (await Json(found)).GetProperty("items").GetArrayLength().Should().Be(1);

        var angelegt = await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "Go in Berlin", skills = new[] { "Go" }, location = "Berlin", remote = true });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        var text = await angelegt.Content.ReadAsStringAsync();

        text.Should().NotContain(subject.ToString(), "eine gespeicherte Suche hält keinen Treffer");
        text.Should().Contain("Go");

        var gelesen = await Json(
            await AlsFirma().GetAsync(new Uri("/scout/searches", UriKind.Relative)));

        gelesen.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("name").GetString().Should().Be("Go in Berlin");

        // Und in der Tabelle steht auch nichts anderes.
        await using var context = Kontext();
        var row = await context.Searches.SingleAsync();
        row.Skills.Should().Equal("Go");
        row.SubjectId.Should().Be(Werber);
    }

    /// <summary>Eine fremde Search gibt es für mich nicht.</summary>
    [Fact]
    public async Task Eine_fremde_Suche_gibt_es_fuer_mich_nicht()
    {
        var angelegt = await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "Meine", skills = new[] { "Rust" } });

        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        var fremder = Mit(Tokenform.Firma(Guid.NewGuid(), Firma));

        var weg = await fremder.DeleteAsync(new Uri($"/scout/searches/{id}", UriKind.Relative));
        weg.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var eigen = await AlsFirma().DeleteAsync(new Uri($"/scout/searches/{id}", UriKind.Relative));
        eigen.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>Ein Name ist Pflicht, und zu viele Worte sind zu viele.</summary>
    [Fact]
    public async Task Eine_unbrauchbare_Anfrage_gibt_422()
    {
        var ohneNamen = await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative), new { name = "", skills = new[] { "Go" } });

        ohneNamen.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var zuViele = await AlsFirma().GetAsync(new Uri(
            "/scout/candidates?" + string.Join(
                '&', Enumerable.Range(0, 11).Select(nummer => $"skill=wort{nummer}")),
            UriKind.Relative));

        zuViele.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ---------------------------------------------------------------------
    // Die Ansprache
    // ---------------------------------------------------------------------

    /// <summary>Der Entwurf kommt zurück — und nichts davon bleibt hier.</summary>
    /// <remarks>
    /// Die Zusage ist eine Abwesenheit, und sie wird als solche gemessen: nach
    /// dem Entwurf steht in der Datenbank keine Zeile mehr als vorher — kein
    /// Vermerk, dass jemand angesprochen werden sollte, und schon gar keine
    /// Nachricht an die angesprochene Person (ADR-0036 Auflage 4, ADR-0024).
    /// </remarks>
    [Fact]
    public async Task Eine_Ansprache_wird_entworfen_und_nichts_davon_bleibt()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject, ["Go"]));
        _tor.Frei.Add((subject, Firma));

        var vorher = (await Postausgang()).Count;

        var response = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{subject}/draft", UriKind.Relative),
            new { skills = new[] { "Go" }, wish = "kurz" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await Json(response);
        body.GetProperty("draft").GetString().Should().NotBeNullOrEmpty();
        body.EnumerateObject().Select(field => field.Name).Should().Equal("draft");

        (await Postausgang()).Count.Should().Be(
            vorher, "ein Entwurf schickt niemandem etwas — auch keine Benachrichtigung");

        await using var context = Kontext();
        (await context.Searches.CountAsync()).Should().Be(0, "ein Entwurf wird nicht abgelegt");
    }

    /// <summary>Wer nichts freigegeben hat, ist kein Ansprechpartner: 404.</summary>
    /// <remarks>
    /// Verborgen und nicht vorhanden antworten gleich — ein Unterschied sagte,
    /// ob dieser Mensch hier ist.
    /// </remarks>
    [Fact]
    public async Task Ohne_Freigabe_gibt_es_niemanden_anzusprechen()
    {
        var verborgen = Guid.NewGuid();
        _suche.Bestand.Add(Profile(verborgen, ["Go"]));

        var response = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{verborgen}/draft", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var gibtEsNicht = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{Guid.NewGuid()}/draft", UriKind.Relative), new { });

        gibtEsNicht.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _entwerfer.Letzte.Should().BeNull("ohne Freigabe sieht das Modell gar nichts");
    }

    /// <summary>Ohne Anbieter sagt der Dienst das: 503, keine ResumeTemplate.</summary>
    [Fact]
    public async Task Ohne_Anbieter_gibt_es_503_und_keine_Vorlage()
    {
        var subject = Guid.NewGuid();
        _suche.Bestand.Add(Profile(subject, ["Go"]));
        _tor.Frei.Add((subject, Firma));
        _entwerfer.Fehlt = true;

        var response = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{subject}/draft", UriKind.Relative), new { });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("draft");
    }

    // ---------------------------------------------------------------------
    // Die Löschung
    // ---------------------------------------------------------------------

    /// <summary>Die Kaskade nimmt Searches UND Vermerke mit.</summary>
    /// <remarks>
    /// Beides, und beides ist personenbezogen: die gespeicherte Search gehört
    /// dem Menschen, der sie abgelegt hat; die Ausgangszeile handelt von einem
    /// Menschen. Wer nur die eine löscht, hat die Zusage aus ADR-0027 zur
    /// Hälfte eingelöst — und niemand sieht es.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_Suchen_und_Vermerke_mit()
    {
        var gefundener = Guid.NewGuid();
        _suche.Bestand.Add(Profile(gefundener));
        _tor.Frei.Add((gefundener, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "bleibt nicht", skills = new[] { "Go" } });

        (await Postausgang()).Should().Contain(row => row.UserId == gefundener);

        // Der Mensch, der gefunden wurde.
        (await Loesche(gefundener)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Postausgang()).Should().NotContain(row => row.UserId == gefundener);

        // Und der Mensch, der gesucht hat.
        (await Loesche(Werber)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var context = Kontext();
        (await context.Searches.CountAsync(row => row.SubjectId == Werber)).Should().Be(0);
    }

    /// <summary>Ohne das Geheimnis löscht niemand.</summary>
    [Fact]
    public async Task Ohne_Geheimnis_loescht_niemand()
    {
        var response = await _dienst.CreateClient().PostAsJsonAsync(
            new Uri("/erasure", UriKind.Relative), new { user_id = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------------

    private HttpClient AlsPerson() => Mit(Tokenform.Person(Werber));

    private HttpClient AlsFirma() => Mit(Tokenform.Firma(Werber, Firma));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private Task<HttpResponseMessage> Loesche(Guid subject)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);

        return browser.PostAsJsonAsync(new Uri("/erasure", UriKind.Relative), new { user_id = subject });
    }

    private ScoutDbContext Kontext() =>
        new((DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Configure(
            new DbContextOptionsBuilder<ScoutDbContext>(),
            ScoutDbContextFactory.DataSource(postgres.ConnectionString)).Options);

    private async Task<List<OutboxRow>> Postausgang()
    {
        await using var context = Kontext();

        return await context.Set<OutboxRow>().ToListAsync();
    }

    private static ProfileHit Profile(Guid subject, string[]? genannt = null) =>
        new(new SubjectId(subject), "Entwicklerin", "Ich schweisse.", "Berlin", true, genannt ?? ["Go"]);

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
