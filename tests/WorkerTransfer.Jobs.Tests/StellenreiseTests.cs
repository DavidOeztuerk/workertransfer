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

namespace WorkerTransfer.Jobs.Tests;

/// <summary>Ein Entwerfer, der aufschreibt statt zu fragen.</summary>
public sealed class Probeentwerfer : IEntwerfer
{
    /// <summary>Was zuletzt hinausgegangen wäre — das ist die interessante Frage.</summary>
    public Anzeigenentwurf? Letzter { get; private set; }

    public Task<string> EntwirfAsync(
        Anzeigenentwurf entwurf, CancellationToken cancellationToken = default)
    {
        Letzter = entwurf;
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

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", Rueckzugsgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IEntwerfer>(_ => _entwerfer)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid firma) => Mit(Tokenform.Firma(Guid.CreateVersion7(), firma));

    private HttpClient AlsPerson() => Mit(Tokenform.Person(Guid.CreateVersion7()));

    private HttpClient Mit(string token)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Schreibe(
        HttpClient browser, string titel = "Entwicklerin", params string[] faehigkeiten) =>
        browser.PostAsJsonAsync("/jobs", new
        {
            title = titel,
            description = "Wir bauen verteilte Systeme.",
            location = "Berlin",
            remote_mode = "hybrid",
            employment_type = "full_time",
            skills = faehigkeiten.Length > 0 ? faehigkeiten : ["C#", "Postgres"]
        });

    private async Task<Guid> Veroeffentlicht(HttpClient browser, params string[] faehigkeiten)
    {
        var angelegt = await Schreibe(browser, faehigkeiten: faehigkeiten);
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        (await browser.PostAsync($"/jobs/{id}/publish", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return id;
    }

    /// <summary>
    /// Wer eine Anzeige schreibt, soll sie lesen können, bevor Bewerbungen
    /// darauf eingehen.
    /// </summary>
    [Fact]
    public async Task Eine_neue_Anzeige_ist_ein_Entwurf_und_nicht_sichtbar()
    {
        var firma = Guid.CreateVersion7();
        var angelegt = await Schreibe(AlsFirma(firma));

        angelegt.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(angelegt)).GetProperty("status").GetString().Should().Be("draft");

        var id = (await Json(angelegt)).GetProperty("id").GetGuid();
        var oeffentlich = await Json(await AlsPerson().GetAsync("/jobs"));

        oeffentlich.GetProperty("items").EnumerateArray()
            .Should().NotContain(eintrag => eintrag.GetProperty("id").GetGuid() == id);
    }

    /// <summary>
    /// Sonst verriete der Dienst, dass ein Unternehmen gerade etwas schreibt.
    /// </summary>
    [Fact]
    public async Task Ein_fremder_Entwurf_sieht_aus_wie_keine_Anzeige()
    {
        var angelegt = await Schreibe(AlsFirma(Guid.CreateVersion7()));
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
        var firma = Guid.CreateVersion7();
        var id = await Veroeffentlicht(AlsFirma(firma));

        var gefunden = await Json(await AlsPerson().GetAsync("/jobs"));

        gefunden.GetProperty("items").EnumerateArray()
            .Should().Contain(eintrag => eintrag.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Eine_fremde_Anzeige_laesst_sich_nicht_aendern()
    {
        var id = await Veroeffentlicht(AlsFirma(Guid.CreateVersion7()));

        var versuch = await AlsFirma(Guid.CreateVersion7()).PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Uebernommen", description = "Nicht meine Anzeige."
        });

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
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

        var versuch = await browser.PutAsJsonAsync($"/jobs/{id}", new
        {
            title = "Doch anders", description = "Nachtraeglich."
        });

        versuch.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Eine_Privatperson_schreibt_keine_Anzeige()
    {
        var versuch = await Schreibe(AlsPerson());

        versuch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Erst umbenennen, dann entdoppeln (ADR-0023). Andersherum zählte die
    /// Anzeige eine Anforderung doppelt.
    /// </summary>
    [Fact]
    public async Task Postgres_und_PostgreSQL_sind_eine_Anforderung()
    {
        var angelegt = await Schreibe(
            AlsFirma(Guid.CreateVersion7()), faehigkeiten: ["Postgres", "PostgreSQL", "C#"]);

        var faehigkeiten = (await Json(angelegt)).GetProperty("skills");

        faehigkeiten.GetArrayLength().Should().Be(2);
    }

    /// <summary>
    /// Eine Aussage über Sprache ist erlaubt; eine über einen Menschen nicht.
    /// „React" heißt nicht, dass jemand JavaScript kann.
    /// </summary>
    [Fact]
    public async Task Aus_React_wird_kein_JavaScript()
    {
        var angelegt = await Schreibe(AlsFirma(Guid.CreateVersion7()), faehigkeiten: ["React"]);

        var faehigkeiten = (await Json(angelegt)).GetProperty("skills")
            .EnumerateArray().Select(eintrag => eintrag.GetString()).ToList();

        faehigkeiten.Should().ContainSingle();
        faehigkeiten.Should().NotContain("JavaScript");
    }

    /// <summary>
    /// Eine Anforderung, die länger ist, als eine Person sie überhaupt
    /// eintragen kann, wäre garantiert nie ein Treffer — eine Zeile, die für
    /// niemanden je ein Haken werden kann.
    /// </summary>
    [Fact]
    public void Eine_Stelle_darf_keine_Faehigkeit_verlangen_die_kein_Profil_haelt()
    {
        Domain.Stellen.Faehigkeitenliste.Hoechstlaenge
            .Should().BeLessThanOrEqualTo(
                Profile.Domain.Faehigkeiten.Faehigkeitenliste.Hoechstlaenge);
    }

    [Fact]
    public async Task Mehr_als_zwanzig_Anforderungen_werden_abgewiesen()
    {
        var zuViele = Enumerable.Range(0, 21).Select(nummer => $"Faehigkeit{nummer}").ToArray();

        var versuch = await Schreibe(AlsFirma(Guid.CreateVersion7()), faehigkeiten: zuViele);

        versuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Er hilft einem Unternehmen, seine EIGENE Anzeige zu formulieren, und
    /// sagt nie etwas über jemanden (ADR-0024).
    /// </summary>
    [Fact]
    public async Task Der_Entwurf_traegt_weder_Mandant_noch_Firmennamen()
    {
        var firma = Guid.CreateVersion7();

        var entworfen = await AlsFirma(firma).PostAsJsonAsync("/jobs/draft", new
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

        prompt.Should().NotContain(firma.ToString(), "keine tenant_id");
        prompt.Should().Contain("kuerzer");
    }

    /// <summary>
    /// Keine Aussagen über Bewerberinnen und Bewerber — die Regeln sagen es
    /// ausdrücklich, weil eine Anzeige der Ort ist, an dem so etwas sonst
    /// hineinrutscht.
    /// </summary>
    [Fact]
    public void Die_Entwurfsregeln_verbieten_Aussagen_ueber_Menschen()
    {
        Anzeigenentwurf.Regeln.Should().Contain("Erfinde NICHTS hinzu");
        Anzeigenentwurf.Regeln.Should().Contain("Keine Aussagen über Bewerberinnen");
    }

    /// <summary>
    /// Er hält nichts über eine natürliche Person (ADR-0027 §2). Ein
    /// Löschbefehl an ihn wäre ein Endpunkt, der „erledigt" sagt, ohne je etwas
    /// zu tun.
    /// </summary>
    [Fact]
    public async Task Es_gibt_keinen_Loeschendpunkt()
    {
        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        anfrage.Headers.Add("X-Erasure-Secret", Rueckzugsgeheimnis);

        var versuch = await _dienst.CreateClient().SendAsync(anfrage);

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Zurückgezogen, nicht gelöscht: ein Unternehmen ist keine natürliche
    /// Person, und seine Anzeigen gehören ihm auch dann noch. Aber eine
    /// unbeaufsichtigte Anzeige ist schlechter als keine.
    /// </summary>
    [Fact]
    public async Task Der_Unternehmensrueckzug_schliesst_alle_offenen_Anzeigen()
    {
        var firma = Guid.CreateVersion7();
        var browser = AlsFirma(firma);

        await Veroeffentlicht(browser);
        await Schreibe(browser, "Ein Entwurf");

        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = firma })
        };
        anfrage.Headers.Add("X-Erasure-Secret", Rueckzugsgeheimnis);

        var zurueck = await _dienst.CreateClient().SendAsync(anfrage);

        zurueck.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(zurueck)).GetProperty("withdrawn").GetInt32().Should().Be(2);

        var meine = await Json(await browser.GetAsync("/companies/me/jobs"));

        meine.EnumerateArray()
            .Should().OnlyContain(eintrag => eintrag.GetProperty("status").GetString() == "closed");
    }

    [Fact]
    public async Task Ohne_das_richtige_Geheimnis_wird_nichts_zurueckgezogen()
    {
        var firma = Guid.CreateVersion7();
        await Veroeffentlicht(AlsFirma(firma));

        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/companies/withdrawal")
        {
            Content = JsonContent.Create(new { tenant_id = firma })
        };
        anfrage.Headers.Add("X-Erasure-Secret", "falsch");

        var versuch = await _dienst.CreateClient().SendAsync(anfrage);

        versuch.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Kein Punktwert, kein Rang, kein Prozentwert — die Passung rechnet der
    /// Browser und zeigt sie der Person (ADR-0022).
    /// </summary>
    [Fact]
    public async Task Keine_Anzeige_traegt_eine_Punktzahl()
    {
        await Veroeffentlicht(AlsFirma(Guid.CreateVersion7()));

        var rumpf = await (await AlsPerson().GetAsync("/jobs")).Content.ReadAsStringAsync();

        foreach (var verboten in new[] { "score", "rank", "match", "percent", "weight" })
        {
            rumpf.Should().NotContain(verboten);
        }
    }

    /// <summary>
    /// Der Stand wird im Speicher gefiltert, nicht vom Aufrufer: ein
    /// <c>status=draft</c> auf der Leitung wäre ein Weg, fremde Entwürfe zu
    /// lesen.
    /// </summary>
    [Fact]
    public async Task Ein_Standparameter_oeffnet_keine_fremden_Entwuerfe()
    {
        var angelegt = await Schreibe(AlsFirma(Guid.CreateVersion7()));
        var id = (await Json(angelegt)).GetProperty("id").GetGuid();

        var seite = await Json(await AlsPerson().GetAsync("/jobs?status=draft"));

        seite.GetProperty("items").EnumerateArray()
            .Should().NotContain(eintrag => eintrag.GetProperty("id").GetGuid() == id);
    }
}
