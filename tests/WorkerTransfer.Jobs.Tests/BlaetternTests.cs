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

/// <summary>Die Stellenliste blättert nach Seitennummern.</summary>
/// <remarks>
/// Diese Reihe hält den Vertrag fest, weil er sich geändert hat: vorher gab
/// <c>GET /jobs</c> einen Zeiger (<c>next</c>) heraus, jetzt eine Seitennummer
/// samt Gesamtzahl. Der CI-Auftrag <c>images</c> prüft genau diese Gestalt, und
/// die Routenkarte auch — ein stiller Rückfall auf das alte Feld wäre in beiden
/// nicht zu sehen.
/// <para>
/// Der wichtigste Fall ist der letzte: <strong>ein Fähigkeitsfilter muss die
/// GEFILTERTE Menge zählen.</strong> Weil dieser Filter im Speicher läuft (der
/// Wortschatz steht im Code, nicht in SQL — ADR-0023), lag es nahe, erst zu
/// schneiden und dann zu filtern. Dann stünde „Seite 1 von 9" über drei
/// Treffern, und die Blätterleiste böte acht leere Seiten an.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class BlaetternTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _dienst = null!;

    public Task InitializeAsync()
    {
        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Erasure:Geheimnis", "rueckzug-geheimnis");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IEntwerfer>(_ => new Probeentwerfer())));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AlsFirma(Guid firma)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Tokenform.Firma(Guid.CreateVersion7(), firma));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Legt <paramref name="wieViele"/> veröffentlichte Anzeigen an.</summary>
    /// <remarks>
    /// Das <paramref name="kennwort"/> steht in der Beschreibung und lässt sich
    /// als <c>q</c> zurückgeben. Alle Reihen dieser Sammlung teilen sich EINE
    /// Datenbank — wer eine absolute Gesamtzahl prüft, muss seine eigenen Zeilen
    /// einklammern, sonst zählt er die einer anderen Reihe mit und ist nur so
    /// lange grün, wie niemand sonst dieselbe Fähigkeit schreibt.
    /// </remarks>
    private async Task LegeAn(
        HttpClient browser, int wieViele, string kennwort = "", params string[] faehigkeiten)
    {
        for (var i = 0; i < wieViele; i++)
        {
            var angelegt = await browser.PostAsJsonAsync("/jobs", new
            {
                title = $"Stelle {Guid.NewGuid():N}",
                description = $"Wir bauen verteilte Systeme. {kennwort}",
                location = "Berlin",
                remote_mode = "hybrid",
                employment_type = "full_time",
                skills = faehigkeiten.Length > 0 ? faehigkeiten : ["C#"]
            });

            var id = (await Json(angelegt)).GetProperty("id").GetGuid();
            await browser.PostAsync($"/jobs/{id}/publish", null);
        }
    }

    /// <summary>Die Antwort trägt alles, was eine Blätterleiste braucht.</summary>
    [Fact]
    public async Task Die_Antwort_traegt_die_ganze_Blaetterleiste()
    {
        await LegeAn(AlsFirma(Guid.CreateVersion7()), 1);

        var seite = await Json(await _dienst.CreateClient().GetAsync("/jobs?page=1"));

        seite.TryGetProperty("items", out _).Should().BeTrue();
        seite.GetProperty("page").GetInt32().Should().Be(1);
        seite.GetProperty("page_size").GetInt32().Should().Be(12);
        seite.TryGetProperty("total_items", out _).Should().BeTrue();
        seite.TryGetProperty("total_pages", out _).Should().BeTrue();
        seite.TryGetProperty("has_next", out _).Should().BeTrue();
        seite.TryGetProperty("has_previous", out _).Should().BeTrue();

        // Der Zeiger ist weg und darf nicht zurückkommen: zwei Wege durch
        // dieselbe Liste sind zwei Wahrheiten über ihre Reihenfolge.
        seite.TryGetProperty("next", out _).Should().BeFalse();
    }

    /// <summary>Zwölf ohne Angabe — und nicht alles.</summary>
    /// <remarks>
    /// Der Anlass für das ganze Blättern: die Liste gab vorher zwanzig heraus
    /// und bot keinen Weg zum Rest.
    /// </remarks>
    [Fact]
    public async Task Ohne_Angabe_sind_es_zwoelf()
    {
        await LegeAn(AlsFirma(Guid.CreateVersion7()), 14);

        var seite = await Json(await _dienst.CreateClient().GetAsync("/jobs"));

        seite.GetProperty("items").GetArrayLength().Should().Be(12);
        seite.GetProperty("has_next").GetBoolean().Should().BeTrue();
        seite.GetProperty("has_previous").GetBoolean().Should().BeFalse();
    }

    /// <summary>Die zweite Seite ist eine andere und überschneidet sich nicht.</summary>
    [Fact]
    public async Task Die_zweite_Seite_wiederholt_die_erste_nicht()
    {
        await LegeAn(AlsFirma(Guid.CreateVersion7()), 8);

        var browser = _dienst.CreateClient();
        var erste = await Json(await browser.GetAsync("/jobs?page=1&page_size=5"));
        var zweite = await Json(await browser.GetAsync("/jobs?page=2&page_size=5"));

        static IEnumerable<Guid> Kennungen(JsonElement seite) =>
            seite.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

        zweite.GetProperty("page").GetInt32().Should().Be(2);
        zweite.GetProperty("has_previous").GetBoolean().Should().BeTrue();
        Kennungen(erste).Should().NotIntersectWith(Kennungen(zweite));
    }

    /// <summary>Unsinn in der Abfrage wird zur Vorgabe, nicht zum Fehler.</summary>
    /// <remarks>
    /// Eine Liste, die auf <c>?page=abc</c> mit 400 antwortet, ist an einer
    /// Stelle streng, an der niemand etwas gewinnt. Und die Obergrenze ist kein
    /// Geschmack: <c>page_size=100000</c> ist keine Anfrage, sondern ein Weg,
    /// die Datenbank zu beschäftigen.
    /// </remarks>
    [Theory]
    [InlineData("?page=abc&page_size=xyz", 1, 12)]
    [InlineData("?page=-3", 1, 12)]
    [InlineData("?page_size=100000", 1, 96)]
    [InlineData("?page_size=0", 1, 12)]
    public async Task Unsinn_wird_zur_Vorgabe(string abfrage, int seiteSoll, int groesseSoll)
    {
        var seite = await Json(await _dienst.CreateClient().GetAsync($"/jobs{abfrage}"));

        seite.GetProperty("page").GetInt32().Should().Be(seiteSoll);
        seite.GetProperty("page_size").GetInt32().Should().Be(groesseSoll);
    }

    /// <summary>Ein Fähigkeitsfilter zählt, was er trifft — nicht, was es gibt.</summary>
    /// <remarks>
    /// Der Filter läuft im Speicher, weil „Postgres" und „PostgreSQL" dieselbe
    /// Anforderung sind und diese Regel im Code steht (ADR-0023). Wer erst
    /// schneidet und dann filtert, bekommt eine Gesamtzahl über die ungefilterte
    /// Menge — und die Blätterleiste bietet Seiten an, auf denen nichts steht.
    /// </remarks>
    [Fact]
    public async Task Ein_Faehigkeitsfilter_zaehlt_die_gefilterte_Menge()
    {
        var firma = AlsFirma(Guid.CreateVersion7());
        var kennwort = $"faehigkeit{Guid.NewGuid():N}";
        await LegeAn(firma, 3, kennwort, "Rust");
        await LegeAn(firma, 9, kennwort, "Cobol");

        var seite = await Json(
            await _dienst.CreateClient().GetAsync(
                $"/jobs?q={kennwort}&skill=Rust&page_size=2"));

        seite.GetProperty("total_items").GetInt32().Should().Be(3);
        seite.GetProperty("total_pages").GetInt32().Should().Be(2);
        seite.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    /// <summary>Eine leere Liste hat eine Seite, nicht null.</summary>
    /// <remarks>
    /// „Seite 1 von 0" ist eine Aussage, die niemand lesen kann, und ein Pager
    /// mit null Seiten hat keinen Zustand, den er zeichnen könnte.
    /// </remarks>
    [Fact]
    public async Task Eine_leere_Liste_hat_eine_Seite()
    {
        var seite = await Json(
            await _dienst.CreateClient().GetAsync("/jobs?q=gibtesnichtxyzq"));

        seite.GetProperty("total_items").GetInt32().Should().Be(0);
        seite.GetProperty("total_pages").GetInt32().Should().Be(1);
        seite.GetProperty("has_next").GetBoolean().Should().BeFalse();
    }
}
