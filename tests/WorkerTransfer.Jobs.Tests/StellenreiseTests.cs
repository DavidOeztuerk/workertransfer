using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>Ein Entwerfer, der aufschreibt statt zu fragen.</summary>
public sealed class Probeentwerfer : IDrafter
{
    /// <summary>Was zuletzt hinausgegangen wäre — das ist die interessante Frage.</summary>
    public AdvertDraft? Letzter { get; private set; }

    public Task<string> DraftAsync(
        AdvertDraft draft, CancellationToken cancellationToken = default)
    {
        Letzter = draft;
        return Task.FromResult("Wir suchen jemanden für verteilte Systeme.");
    }
}

/// <summary>Schreiben, veröffentlichen, finden, zurückziehen.</summary>
[Collection(PostgresCollection.Name)]
public class StellenreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Rueckzugsgeheimnis = "rueckzug-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probeentwerfer _entwerfer = new();
    private readonly Rollenprobe _rollen = new();

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", Tokenform.OeffentlichSchluessel);
            host.UseSetting("Jwt:KeyId", Tokenform.Kennung);
            host.UseSetting(SecretStore.Variable, "test-hauptschluessel-fuer-die-reihe");
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Rueckzugsgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => _entwerfer));
                // Der Draht zu identity-service. Die Vorgabe ist `admin`, damit
                // die bestehenden Reisen weiter das messen, wofuer sie da sind;
                // `Rollenreise` dreht ihn auf `member` und prueft die Rolle.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => _rollen));
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

    private HttpClient AlsPerson() => Mit(Tokenform.Person(Guid.CreateVersion7()));

    /// <summary>Ohne Token — so, wie ein anonymer Besucher fragt.</summary>
    private HttpClient Ohne() => _dienst.CreateClient();

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Write(
        HttpClient browser, string titel = "Entwicklerin", params string[] skills) =>
        browser.PostAsJsonAsync("/jobs", new
        {
            title = titel,
            description = "Wir bauen verteilte Systeme.",
            location = "Berlin",
            remote_mode = "hybrid",
            employment_type = "full_time",
            skills = skills.Length > 0 ? skills : ["C#", "Postgres"]
        });

    private async Task<Guid> Veroeffentlicht(HttpClient browser, params string[] skills)
    {
        var angelegt = await Write(browser, skills: skills);
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await browser.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    /// <summary>
    /// Wer eine Anzeige schreibt, soll sie lesen können, bevor Applications
    /// darauf eingehen.
    /// </summary>
    [Fact]
    public async Task Eine_neue_Anzeige_ist_ein_Entwurf_und_nicht_sichtbar()
    {
        var company = Guid.CreateVersion7();
        var angelegt = await Write(AlsFirma(company));

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(angelegt)).GetProperty("status").GetString().Should().Be("draft");

        var id = (await Json(angelegt)).GetProperty("id").GetGuid();
        var oeffentlich = await Json(await AlsPerson().GetAsync("/jobs"));

        oeffentlich.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Sonst verriete der Dienst, dass ein Unternehmen gerade etwas schreibt.
    /// </summary>
    [Fact]
    public async Task Ein_fremder_Entwurf_sieht_aus_wie_keine_Anzeige()
    {
        var angelegt = await Write(AlsFirma(Guid.CreateVersion7()));
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();
        var erfunden = Guid.CreateVersion7();

        var browser = AlsFirma(Guid.CreateVersion7());

        var beiEntwurf = await browser.GetAsync($"/jobs/{id}");
        var beiErfundener = await browser.GetAsync($"/jobs/{erfunden}");

        beiEntwurf.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(beiEntwurf.StatusCode);
    }

    [Fact]
    public async Task Veroeffentlichen_macht_sie_findbar()
    {
        var company = Guid.CreateVersion7();
        var id = await Veroeffentlicht(AlsFirma(company));

        var found = await Json(await AlsPerson().GetAsync("/jobs"));

        found.GetProperty("items").EnumerateArray()
            .Should().Contain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Eine veröffentlichte Anzeige findet auch, wer kein Konto hat.
    /// </summary>
    /// <remarks>
    /// <para>Hier stand eine Sperre auf <c>akteur.Current is null</c>, und sie
    /// war eine Kostenstelle ohne Gegenwert: der Speicher filtert ohnehin auf
    /// <c>status == "published"</c>, und veröffentlicht heisst in dieser Domäne
    /// „für alle sichtbar, auch ohne Konto".</para>
    ///
    /// <para>Bezahlt hat sie die Karriereseite. <c>/careers/&lt;kürzel&gt;</c>
    /// ist in <c>docs/routenkarte.yml</c> ausdrücklich als öffentlich
    /// beschrieben und zeigte einem anonymen Besucher trotzdem keine einzige
    /// Job — Name und Beschreibung des Unternehmens ja, seine Anzeigen
    /// nicht.</para>
    /// </remarks>
    [Fact]
    public async Task Eine_veroeffentlichte_Anzeige_sieht_auch_wer_kein_Konto_hat()
    {
        var id = await Veroeffentlicht(AlsFirma(Guid.CreateVersion7()));

        var response = await Ohne().GetAsync("/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(response)).GetProperty("items").EnumerateArray()
            .Should().Contain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>Ein Entwurf bleibt auch für einen anonymen Aufrufer drinnen.</summary>
    /// <remarks>
    /// Die Gegenprobe zur Öffnung. Der State wird im Speicher gefiltert und nie
    /// vom Aufrufer — ein <c>status=draft</c> auf der Leitung wäre sonst der Weg
    /// zu fremden Entwürfen.
    /// </remarks>
    [Fact]
    public async Task Ein_Entwurf_bleibt_auch_ohne_Konto_verborgen()
    {
        var angelegt = await Write(AlsFirma(Guid.CreateVersion7()), "Geheimer Entwurf");
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        var found = await Json(await Ohne().GetAsync("/jobs"));

        found.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Eine einzelne Anzeige liest auch, wer kein Konto hat — und ein Entwurf
    /// bleibt dabei ununterscheidbar von „gibt es nicht".
    /// </summary>
    /// <remarks>
    /// Der Endpunkt war hinter der Anmeldung, und das kostete zweierlei. Die
    /// Karriereseite ist eine Adresse zum Weitergeben: wer sie öffnet, hat kein
    /// Konto, und ein 401 machte aus der Anzeige eine Anmeldeaufforderung. Und
    /// <c>applications-service</c> fragt hier Dienst-zu-Dienst nach, ob es die
    /// Job gibt — ohne Token, weil er keines hat. Gemessen: <b>jede</b>
    /// Application endete mit 503, und im Protokoll stand „jobs-service
    /// antwortete mit 401".
    /// <para>
    /// Der zweite Teil ist die Gegenprobe zur Öffnung und der wichtigere: ein
    /// Entwurf darf nicht 403 antworten, denn ein 403 neben einem 404 sagt
    /// „hier ist etwas, du darfst nur nicht" — und damit liesse sich abfragen,
    /// welche Kennungen belegt sind.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Eine_Anzeige_liest_auch_wer_kein_Konto_hat()
    {
        var company = Guid.CreateVersion7();
        var draft = (await Json(await Write(AlsFirma(company), "Geheimer Entwurf")))
            .GetProperty("id").GetGuid();
        var offen = (await Json(await Write(AlsFirma(company), "Offene Stelle")))
            .GetProperty("id").GetGuid();
        (await AlsFirma(company).PostAsync($"/jobs/{offen}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var sichtbar = await Ohne().GetAsync($"/jobs/{offen}");
        var beiEntwurf = await Ohne().GetAsync($"/jobs/{draft}");
        var beiErfundener = await Ohne().GetAsync($"/jobs/{Guid.CreateVersion7()}");

        sichtbar.StatusCode.Should().Be(
            HttpStatusCode.OK, "eine veroeffentlichte Anzeige ist oeffentlich");
        (await Json(sichtbar)).GetProperty("title").GetString().Should().Be("Offene Stelle");

        beiEntwurf.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundener.StatusCode.Should().Be(
            beiEntwurf.StatusCode,
            "ein Entwurf und eine erfundene Kennung muessen gleich antworten");
    }

    /// <summary>
    /// Ein Suchbegriff, der ein SQL-Wort enthält, wird beantwortet und nicht
    /// abgewiesen.
    /// </summary>
    /// <remarks>
    /// Der Anlass ist Noelias <c>UseInputSanitization()</c>. Sie trifft das
    /// <b>bloße</b> Schlüsselwort an einer Wortgrenze, und ein Bindestrich ist
    /// eine: <c>Union-Investment</c> — eine reale deutsche Fondsgesellschaft —
    /// kam als <b>400</b> zurück, ebenso <c>Select-Kundenberater</c> und
    /// <c>Drop-In-Zentrum</c>. Auf einer Stellenbörse ist das kein Randfall,
    /// sondern eine normale Search, die ohne Erklärung scheitert.
    /// <para>
    /// Die Middleware ist deshalb aus <c>ServiceFoundation.cs</c> entfernt
    /// (<c>bugs/eingabepruefung-weist-gewoehnliche-woerter-ab.md</c>). Dieser
    /// Test ist der Grund, warum das auffällt, wenn jemand sie zurückholt:
    /// dann steht hier eine rote Zeile mit einem Firmennamen darin, statt einer
    /// Search, die im Betrieb still nicht funktioniert.
    /// </para>
    /// <para>
    /// Geprüft wird der <b>Statuscode</b> und nicht das Ergebnis: dass nichts
    /// gefunden wird, ist in Ordnung — dass die Frage nicht gestellt werden
    /// darf, ist es nicht.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Union-Investment")]
    [InlineData("Select-Kundenberater")]
    [InlineData("Drop-In-Zentrum")]
    [InlineData("Update-Managerin")]
    public async Task Ein_Suchbegriff_mit_einem_SQL_Wort_wird_beantwortet(string begriff)
    {
        var response = await Ohne().GetAsync(
            $"/jobs?q={Uri.EscapeDataString(begriff)}");

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "„{0}\" ist ein Suchbegriff und keine Injektion", begriff);
    }

    /// <summary>
    /// <c>company</c> filtert wirklich — sonst zeigt die Karriereseite fremde
    /// Anzeigen unter dem eigenen Namen.
    /// </summary>
    /// <remarks>
    /// Der Parameter wurde von der Oberfläche <strong>seit jeher</strong>
    /// geschickt und vom Dienst nie gelesen. Ohne diesen Test fällt das nicht
    /// auf: die Liste ist nicht leer, sie ist nur falsch — und eine
    /// Karriereseite mit fremden Jobs sieht aus wie eine volle.
    /// </remarks>
    [Fact]
    public async Task Der_Firmenfilter_zeigt_nur_die_eigenen_Anzeigen()
    {
        var meine = Guid.CreateVersion7();
        var fremde = Guid.CreateVersion7();

        var meineId = await Veroeffentlicht(AlsFirma(meine));
        var fremdeId = await Veroeffentlicht(AlsFirma(fremde));

        var found = await Json(await Ohne().GetAsync($"/jobs?company={meine}"));
        var entries = found.GetProperty("items").EnumerateArray().ToList();

        entries.Should().Contain(entry => entry.GetProperty("id").GetGuid() == meineId);
        entries.Should().NotContain(entry => entry.GetProperty("id").GetGuid() == fremdeId);
    }

    /// <summary>Der Suchbegriff sucht — über Titel und Beschreibung.</summary>
    /// <remarks>
    /// Auch <c>q</c> wurde geschickt und nie gelesen: die Suchmaske war
    /// Dekoration. Sie lieferte immer alles, was so lange nicht auffällt, wie
    /// wenige Anzeigen im Bestand sind.
    /// </remarks>
    [Fact]
    public async Task Der_Suchbegriff_trifft_Titel_und_Beschreibung()
    {
        var company = AlsFirma(Guid.CreateVersion7());
        var gesucht = await Veroeffentlicht(company);
        var anderes = (await Json(await Write(company, "Gaertnerin"))).GetProperty("id").GetGuid();
        (await company.PostAsync($"/jobs/{anderes}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var found = await Json(await Ohne().GetAsync("/jobs?q=entwickler"));
        var entries = found.GetProperty("items").EnumerateArray().ToList();

        // Kleingeschrieben gesucht, gross geschrieben gespeichert.
        entries.Should().Contain(entry => entry.GetProperty("id").GetGuid() == gesucht);
        entries.Should().NotContain(entry => entry.GetProperty("id").GetGuid() == anderes);
    }

    [Fact]
    public async Task Eine_fremde_Anzeige_laesst_sich_nicht_aendern()
    {
        var id = await Veroeffentlicht(AlsFirma(Guid.CreateVersion7()));

        var attempt = await AlsFirma(Guid.CreateVersion7()).PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Uebernommen", description = "Nicht meine Anzeige."
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Eine geschlossene Anzeige zu ändern hieße, den Text zu verändern, auf den
    /// sich Menschen beworben haben.
    /// </summary>
    [Fact]
    public async Task Eine_geschlossene_Anzeige_aendert_niemand_mehr()
    {
        var browser = AlsFirma(Guid.CreateVersion7());
        var id = await Veroeffentlicht(browser);

        await browser.PostAsync($"/jobs/{id}/close", null);

        var attempt = await browser.PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Doch anders", description = "Nachtraeglich."
        });

        attempt.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Eine_Privatperson_schreibt_keine_Anzeige()
    {
        var attempt = await Write(AlsPerson());

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Erst umbenennen, dann entdoppeln (ADR-0023). Andersherum zählte die
    /// Anzeige eine Anforderung doppelt.
    /// </summary>
    [Fact]
    public async Task Postgres_und_PostgreSQL_sind_eine_Anforderung()
    {
        var angelegt = await Write(
            AlsFirma(Guid.CreateVersion7()), skills: ["Postgres", "PostgreSQL", "C#"]);

        var skills = (await Json(angelegt)).GetProperty("skills");

        skills.GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Eine Aussage über Sprache ist erlaubt; eine über einen Menschen nicht.
    /// „React" heißt nicht, dass jemand JavaScript kann.
    /// </summary>
    [Fact]
    public async Task Aus_React_wird_kein_JavaScript()
    {
        var angelegt = await Write(AlsFirma(Guid.CreateVersion7()), skills: ["React"]);

        var skills = (await Json(angelegt)).GetProperty("skills")
            .EnumerateArray().Select(entry => entry.GetString()).ToList();

        skills.Should().ContainSingle();
        skills.Should().NotContain("JavaScript");
    }

    /// <summary>
    /// Eine Anforderung, die länger ist, als eine Person sie überhaupt
    /// eintragen kann, wäre garantiert nie ein Hit — eine Zeile, die für
    /// niemanden je ein Haken werden kann.
    /// </summary>
    [Fact]
    public void Eine_Stelle_darf_keine_Faehigkeit_verlangen_die_kein_Profil_haelt()
    {
        Domain.Jobs.SkillList.Hoechstlaenge
            .Should().BeLessThanOrEqualTo(
                Profile.Domain.Faehigkeiten.SkillList.Hoechstlaenge);
    }

    [Fact]
    public async Task Mehr_als_zwanzig_Anforderungen_werden_abgewiesen()
    {
        var zuViele = Enumerable.Range(0, 21).Select(nummer => $"Faehigkeit{nummer}").ToArray();

        var attempt = await Write(AlsFirma(Guid.CreateVersion7()), skills: zuViele);

        attempt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Er hilft einem Unternehmen, seine EIGENE Anzeige zu formulieren, und
    /// sagt nie etwas über jemanden (ADR-0024).
    /// </summary>
    [Fact]
    public async Task Der_Entwurf_traegt_weder_Mandant_noch_Firmennamen()
    {
        var company = Guid.CreateVersion7();

        var entworfen = await AlsFirma(company).PostAsJsonAsync("/jobs/draft", new
        {
            title = "Entwicklerin",
            description = "Wir bauen verteilte Systeme.",
            skills = new[] { "C#" },
            location = "Berlin",
            wish = "kuerzer"
        });

        entworfen.StatusCode.Should().Be(HttpStatusCode.OK);

        _entwerfer.Letzter.Should().NotBeNull();
        var prompt = _entwerfer.Letzter!.Prompt;

        prompt.Should().NotContain(company.ToString(), "keine tenant_id");
        prompt.Should().Contain("kuerzer");
    }

    /// <summary>
    /// Der Wunsch ist begrenzt — und ein zu langer ist eine Eingabe, kein Ausfall.
    /// </summary>
    /// <remarks>
    /// Er war das einzige Feld dieses Endpunkts, das an keinem Wertobjekt
    /// vorbeikommt: Titel, Beschreibung, Ort und Fähigkeiten sind in der Domäne
    /// längst begrenzt, der Wunsch kam roh aus dem Rumpf und ging ungeprüft an
    /// den fremden Anbieter. Geprüft wird beides — dass 501 Zeichen abgelehnt
    /// werden, und dass 500 durchgehen: eine Grenze, die auch das Erlaubte
    /// abweist, merkt man erst an einer echten ResumeRequest.
    /// </remarks>
    [Theory]
    [InlineData(500, HttpStatusCode.OK)]
    [InlineData(501, HttpStatusCode.UnprocessableEntity)]
    public async Task Ein_zu_langer_Wunsch_ist_ein_422(int length, HttpStatusCode erwartet)
    {
        var response = await AlsFirma(Guid.CreateVersion7()).PostAsJsonAsync("/jobs/draft", new
        {
            title = "Entwicklerin",
            description = "Wir bauen verteilte Systeme.",
            skills = new[] { "C#" },
            location = "Berlin",
            wish = new string('a', length)
        });

        response.StatusCode.Should().Be(erwartet);
    }

    /// <summary>
    /// None Aussagen über Bewerberinnen und Bewerber — die Regeln sagen es
    /// ausdrücklich, weil eine Anzeige der Ort ist, an dem so etwas sonst
    /// hineinrutscht.
    /// </summary>
    [Fact]
    public void Die_Entwurfsregeln_verbieten_Aussagen_ueber_Menschen()
    {
        AdvertDraft.Regeln.Should().Contain("Erfinde NICHTS hinzu");
        AdvertDraft.Regeln.Should().Contain("Keine Aussagen über Bewerberinnen");
    }

    /// <summary>
    /// Er hält nichts über eine natürliche Person (ADR-0027 §2). Ein
    /// Löschbefehl an ihn wäre ein Endpunkt, der „erledigt" sagt, ohne je etwas
    /// zu tun.
    /// </summary>
    [Fact]
    public async Task Es_gibt_keinen_Loeschendpunkt()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        request.Headers.Add("X-Erasure-Secret", Rueckzugsgeheimnis);

        var attempt = await _dienst.CreateClient().SendAsync(request);

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Zurückgezogen, nicht gelöscht: ein Unternehmen ist keine natürliche
    /// Person, und seine Anzeigen gehören ihm auch dann noch. Aber eine
    /// unbeaufsichtigte Anzeige ist schlechter als keine.
    /// </summary>
    [Fact]
    public async Task Der_Unternehmensrueckzug_schliesst_alle_offenen_Anzeigen()
    {
        var company = Guid.CreateVersion7();
        var browser = AlsFirma(company);

        await Veroeffentlicht(browser);
        await Write(browser, "Ein Entwurf");

        var request = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = company })
        };
        request.Headers.Add("X-Erasure-Secret", Rueckzugsgeheimnis);

        var zurueck = await _dienst.CreateClient().SendAsync(request);

        zurueck.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(zurueck)).GetProperty("withdrawn").GetInt32().Should().Be(2);

        var meine = await Json(await browser.GetAsync("/companies/me/jobs"));

        meine.EnumerateArray()
            .Should().OnlyContain(entry => entry.GetProperty("status").GetString() == "closed");
    }

    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_zurueckgezogen()
    {
        var company = Guid.CreateVersion7();
        await Veroeffentlicht(AlsFirma(company));

        var request = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = company })
        };
        request.Headers.Add("X-Erasure-Secret", "falsch");

        var attempt = await _dienst.CreateClient().SendAsync(request);

        attempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Kein Punktwert, kein Rang, kein Prozentwert — die Passung rechnet der
    /// Browser und zeigt sie der Person (ADR-0022).
    /// </summary>
    [Fact]
    public async Task Keine_Anzeige_traegt_eine_Punktzahl()
    {
        await Veroeffentlicht(AlsFirma(Guid.CreateVersion7()));

        var body = await (await AlsPerson().GetAsync("/jobs")).Content.ReadAsStringAsync();

        foreach (var verboten in new[] { "score", "rank", "match", "percent", "weight" })
        {
            body.Should().NotContain(verboten);
        }
    }

    /// <summary>
    /// Der State wird im Speicher gefiltert, nicht vom Aufrufer: ein
    /// <c>status=draft</c> auf der Leitung wäre ein Weg, fremde Entwürfe zu
    /// lesen.
    /// </summary>
    [Fact]
    public async Task Ein_Standparameter_oeffnet_keine_fremden_Entwuerfe()
    {
        var angelegt = await Write(AlsFirma(Guid.CreateVersion7()));
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        var page = await Json(await AlsPerson().GetAsync("/jobs?status=draft"));

        page.GetProperty("items").EnumerateArray()
            .Should().NotContain(entry => entry.GetProperty("id").GetGuid() == id);
    }
}
