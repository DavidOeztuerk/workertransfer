using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Outbox;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Treffer;
using WorkerTransfer.Scout.Infrastructure.Persistence;

namespace WorkerTransfer.Scout.Tests;

/// <summary>Suchen, ablegen, ansprechen — und gelöscht werden.</summary>
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
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
            {
                dienste.Replace(ServiceDescriptor.Scoped<IProfilsuche>(_ => _suche));
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor));
                dienste.Replace(ServiceDescriptor.Scoped<IBelege>(_ => _belege));
                dienste.Replace(ServiceDescriptor.Scoped<IEntwerfer>(_ => _entwerfer));
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
    /// Die zweite Hälfte ist die, die man vergisst. Auf einem Transfermarkt ist
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

        var firma = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        firma.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------
    // Der Ledger
    // ---------------------------------------------------------------------

    /// <summary>Der Ledger wird bei JEDEM Aufruf gefragt.</summary>
    /// <remarks>
    /// ADR-0013: ein Widerruf muss beim nächsten Lesen wirken. Ein
    /// Zwischenspeicher wäre hier kein Leistungsdetail, sondern ein Regelbruch —
    /// und er fiele erst auf, wenn jemand widerrufen hat und trotzdem gefunden
    /// wird.
    /// </remarks>
    [Fact]
    public async Task Jeder_Aufruf_fragt_den_Ledger_erneut()
    {
        var wer = Guid.NewGuid();
        _suche.Bestand.Add(Profil(wer));
        _tor.Frei.Add((wer, Firma));

        var vorher = _tor.Fragen;

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (_tor.Fragen - vorher).Should().Be(2);
    }

    /// <summary>Ein schweigender Ledger ist weder ein Ja noch ein Nein: 503.</summary>
    [Fact]
    public async Task Ein_schweigender_Ledger_gibt_503()
    {
        _suche.Bestand.Add(Profil(Guid.NewGuid()));
        _tor.Schweigt = true;

        var antwort = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Json(antwort)).GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Eine Antwort, die nicht zu den Fragen passt, wird nicht geraten.
    /// </summary>
    /// <remarks>
    /// Falsch zuzuordnen hiesse, das Profil der falschen Person zu zeigen. 503
    /// und nicht „so gut es geht".
    /// </remarks>
    [Fact]
    public async Task Eine_unpassende_Ledgerantwort_wird_nicht_geraten()
    {
        _suche.Bestand.Add(Profil(Guid.NewGuid()));
        _suche.Bestand.Add(Profil(Guid.NewGuid()));
        _tor.AntwortetMitLaenge = 1;

        var antwort = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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

        var antwort = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        antwort.StatusCode.Should().Be(
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

        var antwort = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
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

        _suche.Bestand.Add(Profil(frei));
        _suche.Bestand.Add(Profil(verborgen));
        _tor.Frei.Add((frei, Firma));

        var antwort = await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        var rumpf = await Json(antwort);

        rumpf.GetProperty("items").EnumerateArray()
            .Select(eintrag => eintrag.GetProperty("subject_id").GetGuid())
            .Should().Equal(frei);

        rumpf.TryGetProperty("total", out _).Should().BeFalse();
        rumpf.TryGetProperty("count", out _).Should().BeFalse();
        rumpf.EnumerateObject().Select(feld => feld.Name)
            .Should().BeEquivalentTo("items", "next");
    }

    // ---------------------------------------------------------------------
    // „Dein Profil wurde entdeckt"
    // ---------------------------------------------------------------------

    /// <summary>
    /// Wer in einer Suche auftaucht, bekommt einen Vermerk im Postausgang —
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

        _suche.Bestand.Add(Profil(frei));
        _suche.Bestand.Add(Profil(verborgen));
        _tor.Frei.Add((frei, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        var vermerke = await Postausgang();

        vermerke.Where(zeile => zeile.UserId == frei)
            .Should().ContainSingle()
            .Which.Kind.Should().Be("profile_discovered");

        vermerke.Should().NotContain(zeile => zeile.UserId == verborgen);

        // Die Zeile traegt Kennung und Art. Der Mandant steht nirgends darin —
        // „ein Unternehmen" genuegt, und wer sucht, ist eine Aussage ueber das
        // Unternehmen.
        var zeilenfelder = typeof(OutboxZeile).GetProperties().Select(e => e.Name);
        zeilenfelder.Should().NotContain("TenantId");
    }

    /// <summary>Derselbe Mensch bekommt je Seite höchstens einen Vermerk.</summary>
    [Fact]
    public async Task Derselbe_Mensch_bekommt_je_Seite_einen_Vermerk()
    {
        var wer = Guid.NewGuid();

        // Zweimal derselbe Mensch in einer Seite — das kann profile-service
        // nicht liefern, aber der Befehl soll es trotzdem aushalten.
        _suche.Bestand.Add(Profil(wer));
        _suche.Bestand.Add(Profil(wer));
        _tor.Frei.Add((wer, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));

        (await Postausgang()).Count(zeile => zeile.UserId == wer).Should().Be(1);
    }

    // ---------------------------------------------------------------------
    // Die gespeicherte Anfrage
    // ---------------------------------------------------------------------

    /// <summary>Gespeichert wird die Anfrage — und nachweislich kein Ergebnis.</summary>
    /// <remarks>
    /// Die Gegenprobe steckt in der zweiten Hälfte: es wird gesucht, es gibt
    /// einen Treffer, die Suche wird abgelegt — und in der abgelegten Suche
    /// steht der Treffer nicht. Ein gespeichertes Ergebnis über Menschen
    /// veraltete gegen einen Widerruf (ADR-0036 Entscheidung 4).
    /// </remarks>
    [Fact]
    public async Task Gespeichert_wird_die_Anfrage_nie_das_Ergebnis()
    {
        var wer = Guid.NewGuid();
        _suche.Bestand.Add(Profil(wer, ["Go"]));
        _tor.Frei.Add((wer, Firma));

        var gefunden = await AlsFirma().GetAsync(
            new Uri("/scout/candidates?skill=Go", UriKind.Relative));
        (await Json(gefunden)).GetProperty("items").GetArrayLength().Should().Be(1);

        var angelegt = await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "Go in Berlin", skills = new[] { "Go" }, location = "Berlin", remote = true });

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);

        var text = await angelegt.Content.ReadAsStringAsync();

        text.Should().NotContain(wer.ToString(), "eine gespeicherte Suche hält keinen Treffer");
        text.Should().Contain("Go");

        var gelesen = await Json(
            await AlsFirma().GetAsync(new Uri("/scout/searches", UriKind.Relative)));

        gelesen.GetProperty("items").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("name").GetString().Should().Be("Go in Berlin");

        // Und in der Tabelle steht auch nichts anderes.
        await using var kontext = Kontext();
        var zeile = await kontext.Suchen.SingleAsync();
        zeile.Skills.Should().Equal("Go");
        zeile.SubjectId.Should().Be(Werber);
    }

    /// <summary>Eine fremde Suche gibt es für mich nicht.</summary>
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
        var wer = Guid.NewGuid();
        _suche.Bestand.Add(Profil(wer, ["Go"]));
        _tor.Frei.Add((wer, Firma));

        var vorher = (await Postausgang()).Count;

        var antwort = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{wer}/draft", UriKind.Relative),
            new { skills = new[] { "Go" }, wish = "kurz" });

        antwort.StatusCode.Should().Be(HttpStatusCode.OK);

        var rumpf = await Json(antwort);
        rumpf.GetProperty("draft").GetString().Should().NotBeNullOrEmpty();
        rumpf.EnumerateObject().Select(feld => feld.Name).Should().Equal("draft");

        (await Postausgang()).Count.Should().Be(
            vorher, "ein Entwurf schickt niemandem etwas — auch keine Benachrichtigung");

        await using var kontext = Kontext();
        (await kontext.Suchen.CountAsync()).Should().Be(0, "ein Entwurf wird nicht abgelegt");
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
        _suche.Bestand.Add(Profil(verborgen, ["Go"]));

        var antwort = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{verborgen}/draft", UriKind.Relative), new { });

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var gibtEsNicht = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{Guid.NewGuid()}/draft", UriKind.Relative), new { });

        gibtEsNicht.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _entwerfer.Letzte.Should().BeNull("ohne Freigabe sieht das Modell gar nichts");
    }

    /// <summary>Ohne Anbieter sagt der Dienst das: 503, keine Vorlage.</summary>
    [Fact]
    public async Task Ohne_Anbieter_gibt_es_503_und_keine_Vorlage()
    {
        var wer = Guid.NewGuid();
        _suche.Bestand.Add(Profil(wer, ["Go"]));
        _tor.Frei.Add((wer, Firma));
        _entwerfer.Fehlt = true;

        var antwort = await AlsFirma().PostAsJsonAsync(
            new Uri($"/scout/candidates/{wer}/draft", UriKind.Relative), new { });

        antwort.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await antwort.Content.ReadAsStringAsync()).Should().NotContain("draft");
    }

    // ---------------------------------------------------------------------
    // Die Löschung
    // ---------------------------------------------------------------------

    /// <summary>Die Kaskade nimmt Suchen UND Vermerke mit.</summary>
    /// <remarks>
    /// Beides, und beides ist personenbezogen: die gespeicherte Suche gehört
    /// dem Menschen, der sie abgelegt hat; die Ausgangszeile handelt von einem
    /// Menschen. Wer nur die eine löscht, hat die Zusage aus ADR-0027 zur
    /// Hälfte eingelöst — und niemand sieht es.
    /// </remarks>
    [Fact]
    public async Task Die_Loeschung_nimmt_Suchen_und_Vermerke_mit()
    {
        var gefundener = Guid.NewGuid();
        _suche.Bestand.Add(Profil(gefundener));
        _tor.Frei.Add((gefundener, Firma));

        await AlsFirma().GetAsync(new Uri("/scout/candidates", UriKind.Relative));
        await AlsFirma().PostAsJsonAsync(
            new Uri("/scout/searches", UriKind.Relative),
            new { name = "bleibt nicht", skills = new[] { "Go" } });

        (await Postausgang()).Should().Contain(zeile => zeile.UserId == gefundener);

        // Der Mensch, der gefunden wurde.
        (await Loesche(gefundener)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Postausgang()).Should().NotContain(zeile => zeile.UserId == gefundener);

        // Und der Mensch, der gesucht hat.
        (await Loesche(Werber)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var kontext = Kontext();
        (await kontext.Suchen.CountAsync(zeile => zeile.SubjectId == Werber)).Should().Be(0);
    }

    /// <summary>Ohne das Geheimnis löscht niemand.</summary>
    [Fact]
    public async Task Ohne_Geheimnis_loescht_niemand()
    {
        var antwort = await _dienst.CreateClient().PostAsJsonAsync(
            new Uri("/erasure", UriKind.Relative), new { user_id = Guid.NewGuid() });

        antwort.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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

    private Task<HttpResponseMessage> Loesche(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Add("X-Erasure-Secret", Loeschgeheimnis);

        return browser.PostAsJsonAsync(new Uri("/erasure", UriKind.Relative), new { user_id = wer });
    }

    private ScoutDbContext Kontext() =>
        new((DbContextOptions<ScoutDbContext>)ScoutDbContextFactory.Konfiguriere(
            new DbContextOptionsBuilder<ScoutDbContext>(),
            ScoutDbContextFactory.Datenquelle(postgres.ConnectionString)).Options);

    private async Task<List<OutboxZeile>> Postausgang()
    {
        await using var kontext = Kontext();

        return await kontext.Set<OutboxZeile>().ToListAsync();
    }

    private static Profilfund Profil(Guid wer, string[]? genannt = null) =>
        new(new SubjectId(wer), "Entwicklerin", "Ich schweisse.", "Berlin", true, genannt ?? ["Go"]);

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;
}
