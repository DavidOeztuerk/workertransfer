using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Girder.Core.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Portfolio.Application.Ports;

namespace WorkerTransfer.Portfolio.Tests;

/// <summary>Ein Ledger, den der Test steuert.</summary>
public sealed class Probetor : IEinwilligungstor
{
    /// <summary>Wer sein Portfolio freigegeben hat.</summary>
    public HashSet<Guid> Frei { get; } = [];

    /// <summary>Wie oft gefragt wurde.</summary>
    public int Fragen { get; private set; }

    /// <summary>Wenn wahr, sagt der Ledger gar nichts.</summary>
    public bool Schweigt { get; set; }

    public Task<bool> DarfSehenAsync(SubjectId wer, CancellationToken cancellationToken = default)
    {
        Fragen++;
        return Schweigt
            ? throw new EinwilligungSchweigt("der Ledger antwortet im Test nicht")
            : Task.FromResult(Frei.Contains(wer.Value));
    }
}

/// <summary>Schreiben, gesehen werden, Arbeitsproben, gelöscht werden.</summary>
[Collection(PostgresCollection.Name)]
public class PortfolioreiseTests(Postgres postgres) : IAsyncLifetime
{
    private const string Loeschgeheimnis = "loesch-geheimnis";

    private WebApplicationFactory<Program> _dienst = null!;
    private readonly Probetor _tor = new();
    private string _ablage = null!;

    public Task InitializeAsync()
    {
        // Ein eigenes Verzeichnis je Lauf: die Löschung soll nachweisbar
        // Dateien entfernen, und dafür muss man sie zählen können.
        _ablage = Path.Combine(Path.GetTempPath(), $"wt-portfolio-{Guid.CreateVersion7():N}");

        _dienst = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Storage:Wurzel", _ablage);
            host.UseSetting("Erasure:Geheimnis", Loeschgeheimnis);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(dienste =>
                dienste.Replace(ServiceDescriptor.Scoped<IEinwilligungstor>(_ => _tor)));
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _dienst.Dispose();

        if (Directory.Exists(_ablage))
        {
            Directory.Delete(_ablage, recursive: true);
        }

        return Task.CompletedTask;
    }

    private HttpClient Als(Guid wer)
    {
        var browser = _dienst.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Tokenform.Person(wer));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage antwort) =>
        JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Schreibe(
        HttpClient browser, string? anhang = null, string titel = "Ein Projekt") =>
        browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = titel,
                    summary = "Was ich dabei gemacht habe.",
                    url = "https://beispiel.example/projekt",
                    role = "Entwicklerin",
                    year = 2024,
                    attachment = anhang
                }
            }
        });

    /// <summary>
    /// Technologien an einer Arbeit: <strong>vereinheitlicht und entdoppelt</strong>.
    /// </summary>
    /// <remarks>
    /// Derselbe Wortschatz wie im Profil und am Lebenslauf — „postgres" heisst
    /// überall „PostgreSQL" (ADR-0023). Erst vereinheitlichen, dann entdoppeln:
    /// anders herum stünden beide Schreibweisen als zwei Einträge da.
    /// <para>
    /// Der Grund für das Feld: es ist ein Ort, an dem jemand NENNEN kann, was
    /// er kann. Durchsuchbar wird die Arbeit dadurch nicht — suchbar ist nur
    /// das Profil, und dorthin kommt eine Fähigkeit mit einem Klick.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Technologien_werden_vereinheitlicht_und_entdoppelt()
    {
        var browser = Als(Guid.CreateVersion7());

        var geschrieben = await browser.PutAsJsonAsync("/portfolios/me", new
        {
            items = new[]
            {
                new
                {
                    title = "Ein Projekt",
                    summary = string.Empty,
                    url = (string?)null,
                    role = string.Empty,
                    year = (int?)null,
                    attachment = (string?)null,
                    technologies = new[] { "postgres", "PostgreSQL", " Go ", string.Empty }
                }
            }
        });

        geschrieben.StatusCode.Should().Be(HttpStatusCode.OK);

        (await Json(geschrieben)).GetProperty("items")[0]
            .GetProperty("technologies").EnumerateArray()
            .Select(eintrag => eintrag.GetString())
            .Should().Equal("PostgreSQL", "Go");
    }

    /// <summary>Eine Arbeit ohne Technologien ist kein Fehler, sondern leer.</summary>
    /// <remarks>
    /// Ältere Zeilen tragen das Feld gar nicht — sie sollen weiterlesbar sein,
    /// ohne dass jemand eine Nachwanderung fährt.
    /// </remarks>
    [Fact]
    public async Task Ohne_Technologien_bleibt_die_Liste_leer()
    {
        var browser = Als(Guid.CreateVersion7());

        var geschrieben = await Schreibe(browser);

        (await Json(geschrieben)).GetProperty("items")[0]
            .GetProperty("technologies").GetArrayLength().Should().Be(0);
    }

    private async Task<string> LadeHoch(HttpClient browser, string inhalt = "Arbeitsprobe")
    {
        using var formular = new MultipartFormDataContent();
        var datei = new ByteArrayContent(Encoding.UTF8.GetBytes(inhalt));
        datei.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        formular.Add(datei, "file", "probe.pdf");

        var hochgeladen = await browser.PostAsync("/portfolios/me/attachments", formular);
        hochgeladen.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await Json(hochgeladen)).GetProperty("attachment").GetString()!;
    }

    [Fact]
    public async Task Schreiben_und_das_eigene_wiederlesen()
    {
        var anna = Guid.CreateVersion7();
        var browser = Als(anna);

        (await Schreibe(browser)).StatusCode.Should().Be(HttpStatusCode.OK);

        var meins = await Json(await browser.GetAsync("/portfolios/me"));

        meins.GetProperty("items")[0].GetProperty("title").GetString().Should().Be("Ein Projekt");
    }

    /// <summary>
    /// Ob ein Portfolio gezeigt werden darf, steht nur im Ledger. Ein Feld hier
    /// wäre eine zweite Wahrheit (ADR-0020 §6).
    /// </summary>
    [Fact]
    public async Task Das_Portfolio_traegt_keine_Sichtbarkeit()
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(Als(anna));

        var rumpf = await (await Als(anna).GetAsync("/portfolios/me"))
            .Content.ReadAsStringAsync();

        rumpf.Should().NotContain("visibility");
        rumpf.Should().NotContain("is_public");
    }

    [Fact]
    public async Task Verborgen_und_nicht_vorhanden_antworten_gleich()
    {
        var anna = Guid.CreateVersion7();
        var erfunden = Guid.CreateVersion7();
        await Schreibe(Als(anna));

        var browser = Als(Guid.CreateVersion7());

        var beiVerborgenem = await browser.GetAsync($"/portfolios/{anna}");
        var beiErfundenem = await browser.GetAsync($"/portfolios/{erfunden}");

        beiVerborgenem.StatusCode.Should().Be(HttpStatusCode.NotFound);
        beiErfundenem.StatusCode.Should().Be(beiVerborgenem.StatusCode);
    }

    [Fact]
    public async Task Mit_Freigabe_sieht_ein_Fremder_das_Portfolio()
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(Als(anna));
        _tor.Frei.Add(anna);

        var gelesen = await Als(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Schweigt_der_Ledger_wird_nichts_behauptet()
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(Als(anna));
        _tor.Frei.Add(anna);
        _tor.Schweigt = true;

        var versuch = await Als(Guid.CreateVersion7()).GetAsync($"/portfolios/{anna}");

        versuch.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Der_Ledger_wird_bei_jedem_Lesen_neu_gefragt()
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(Als(anna));
        _tor.Frei.Add(anna);

        var browser = Als(Guid.CreateVersion7());
        await browser.GetAsync($"/portfolios/{anna}");
        var nachErstem = _tor.Fragen;

        await browser.GetAsync($"/portfolios/{anna}");

        _tor.Fragen.Should().Be(nachErstem + 1, "nichts wird zwischengespeichert");
    }

    /// <summary>
    /// <b>Dieselbe Einwilligung, kein zweites Tor</b> (ADR-0021). Ein zweites
    /// wirkte auf die Beschreibung, aber nicht auf die Arbeitsprobe.
    /// </summary>
    [Fact]
    public async Task Der_Anhang_haengt_an_derselben_Freigabe_wie_das_Portfolio()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var anhang = await LadeHoch(ihr);
        await Schreibe(ihr, anhang);

        var fremder = Als(Guid.CreateVersion7());

        var verborgen = await fremder.GetAsync($"/portfolios/{anna}/attachments/{anhang}");
        verborgen.StatusCode.Should().Be(HttpStatusCode.NotFound);

        _tor.Frei.Add(anna);

        var frei = await fremder.GetAsync($"/portfolios/{anna}/attachments/{anhang}");
        frei.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Sonst bliebe eine aus dem Portfolio entfernte Arbeitsprobe über ihren
    /// Namen erreichbar, obwohl sie nicht mehr gezeigt wird.
    /// </summary>
    [Fact]
    public async Task Ein_Anhang_den_kein_Eintrag_nennt_ist_nicht_erreichbar()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var anhang = await LadeHoch(ihr);
        await Schreibe(ihr, anhang);
        _tor.Frei.Add(anna);

        // Aus dem Portfolio nehmen, ohne die Datei anzufassen.
        await Schreibe(ihr, anhang: null);

        var versuch = await Als(Guid.CreateVersion7())
            .GetAsync($"/portfolios/{anna}/attachments/{anhang}");

        versuch.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Sonst könnte man seine eigene Arbeitsprobe nicht mehr ansehen, nachdem
    /// man die Freigabe zurückgenommen hat.
    /// </summary>
    [Fact]
    public async Task Das_eigene_liest_man_ohne_Freigabe()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var anhang = await LadeHoch(ihr);
        await Schreibe(ihr, anhang);

        var gelesen = await ihr.GetAsync($"/portfolios/{anna}/attachments/{anhang}");

        gelesen.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Eine hochgeladene Datei, die ein fremder Mensch öffnet, wird
    /// heruntergeladen und nicht in unserem Ursprung ausgeführt.
    /// </summary>
    [Fact]
    public async Task Ein_Anhang_wird_heruntergeladen_und_nicht_angezeigt()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var anhang = await LadeHoch(ihr);
        await Schreibe(ihr, anhang);

        var gelesen = await ihr.GetAsync($"/portfolios/{anna}/attachments/{anhang}");

        gelesen.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        gelesen.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    /// <summary>
    /// Der Weg entsteht aus SubjectId und einem vom Server vergebenen Namen.
    /// Ein Pfad im Namen kommt nie so weit.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..%2F..%2Fetc%2Fpasswd")]
    public async Task Ein_Pfad_statt_eines_Namens_fuehrt_nirgendwohin(string boesartig)
    {
        var anna = Guid.CreateVersion7();
        await Schreibe(Als(anna));
        _tor.Frei.Add(anna);

        var versuch = await Als(Guid.CreateVersion7())
            .GetAsync($"/portfolios/{anna}/attachments/{boesartig}");

        versuch.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Ein Portfolio-Link wird von fremden Menschen angeklickt; javascript: in
    /// einem Feld, das in einem Browser landet, ist der Normalfall eines
    /// Angriffs und kein Randfall.
    /// </summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("file:///etc/passwd")]
    public async Task Nur_http_und_https_sind_erlaubt(string link)
    {
        var versuch = await Als(Guid.CreateVersion7()).PutAsJsonAsync("/portfolios/me", new
        {
            items = new[] { new { title = "Ein Projekt", url = link } }
        });

        versuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Die Zeile ist weg, die Arbeitsprobe auch. Eine Datei, die danach noch
    /// auf der Platte liegt, ist genau das stille Scheitern, gegen das ADR-0027
    /// antritt — niemand sieht sie, weil die Oberfläche sie nicht mehr
    /// verlinkt.
    /// </summary>
    [Fact]
    public async Task Die_Loeschung_nimmt_die_Dateien_mit()
    {
        var anna = Guid.CreateVersion7();
        var ihr = Als(anna);
        var anhang = await LadeHoch(ihr);
        await Schreibe(ihr, anhang);

        var ordner = Path.Combine(_ablage, anna.ToString("N"));
        Directory.Exists(ordner).Should().BeTrue("sonst prüft der Test nichts");

        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = anna })
        };
        anfrage.Headers.Add("X-Erasure-Secret", Loeschgeheimnis);

        var geloescht = await _dienst.CreateClient().SendAsync(anfrage);

        geloescht.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(geloescht)).GetProperty("retained").GetInt32().Should().Be(0);

        var nachher = await ihr.GetAsync("/portfolios/me");
        nachher.StatusCode.Should().Be(HttpStatusCode.OK);
        (await nachher.Content.ReadAsStringAsync()).Trim().Should().Be("null");
        Directory.Exists(ordner).Should().BeFalse("die Arbeitsproben sind mitgegangen");
    }

    [Fact]
    public async Task Ein_leeres_Loeschgeheimnis_schliesst_den_Endpunkt()
    {
        using var ohne = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:portfolio", postgres.ConnectionString);
            host.UseSetting("JwtSettings:Secret", Tokenform.Geheimnis);
            host.UseSetting("JwtSettings:Issuer", Tokenform.Issuer);
            host.UseSetting("JwtSettings:Audience", Tokenform.Audience);
            host.UseSetting("Storage:Wurzel", _ablage);
            host.UseSetting("Erasure:Geheimnis", string.Empty);
            host.UseSetting("environment", "Development");
        });

        var anfrage = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = Guid.CreateVersion7() })
        };
        anfrage.Headers.Add("X-Erasure-Secret", "irgendwas");

        (await ohne.CreateClient().SendAsync(anfrage)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mehr_als_dreissig_Eintraege_werden_abgewiesen()
    {
        var zuViele = Enumerable.Range(0, 31)
            .Select(nummer => new { title = $"Projekt {nummer}" })
            .ToArray();

        var versuch = await Als(Guid.CreateVersion7())
            .PutAsJsonAsync("/portfolios/me", new { items = zuViele });

        versuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
