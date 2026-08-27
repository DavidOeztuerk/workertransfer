using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Die Bremse selbst, ohne Ocelot: was zählt und was nicht.
/// </summary>
/// <remarks>
/// Ein eigener kleiner Wirt statt der ganzen Landschaft, aus einem Grund, der
/// hier der eigentliche Punkt ist: <strong>die Herkunft muss verstellbar sein.</strong>
/// Über einen echten Socket kommt jeder Aufruf von <c>127.0.0.1</c>, und dann
/// ließe sich nie zeigen, dass zwei Herkünfte getrennte Töpfe haben — der Test
/// wäre grün, auch wenn die Bremse alle Menschen in einen Topf würfe. Genau
/// dieser Fehler wäre der schlimme.
/// </remarks>
public sealed class BremseTests
{
    private const string HerkunftsKopf = "X-Test-Herkunft";

    private static WebApplication Wirt(
        int grenze, TimeSpan? fenster = null, bool auchDieProbe = false)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();

        var pfade = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["/auth/login"] = grenze
        };

        if (auchDieProbe)
        {
            pfade["/health/live"] = grenze;
        }

        bau.Services.AddSingleton(new Bremseinstellungen
        {
            Fenster = fenster ?? TimeSpan.FromMinutes(1),
            Pfade = pfade
        });

        var wirt = bau.Build();

        // NUR IM TEST: die Herkunft der Verbindung setzen. Der Erzeuger nimmt
        // sie aus `Connection.RemoteIpAddress`, und über einen echten Socket
        // ist das immer dieselbe Schleife. Das steht VOR der Bremse, damit sie
        // liest, was hier gesetzt wurde.
        wirt.Use(async (kontext, weiter) =>
        {
            if (kontext.Request.Headers.TryGetValue(HerkunftsKopf, out var wert))
            {
                kontext.Connection.RemoteIpAddress =
                    System.Net.IPAddress.Parse(wert.ToString());
            }

            await weiter();
        });

        // Dieselbe Reihenfolge wie in `Program.cs`: die Probe zuerst.
        wirt.UseGesundheit();
        wirt.UseKorrelation();
        wirt.UseBremse();
        wirt.Map("/{**alles}", () => Results.Ok(new { durch = true }));

        return wirt;
    }

    private static async Task<HttpResponseMessage> Anmelden(
        HttpClient browser, string herkunft, string adresse = "anna@example.org")
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = adresse, password = "geheim" }),
                Encoding.UTF8,
                "application/json")
        };
        anfrage.Headers.TryAddWithoutValidation(HerkunftsKopf, herkunft);

        return await browser.SendAsync(anfrage);
    }

    private static HttpClient Browser(WebApplication wirt) =>
        new() { BaseAddress = new Uri(wirt.Urls.First()) };

    [Fact]
    public async Task Ueber_der_Grenze_kommt_429()
    {
        await using var wirt = Wirt(grenze: 3);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var antwort = await Anmelden(browser, "10.0.0.1");
            gesehen.Add(antwort.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Die Zusage, um die es geht: <strong>je Herkunft, nie je Adresse.</strong>
    /// </summary>
    /// <remarks>
    /// Fünf Anmeldeversuche mit fünf <em>verschiedenen</em> Adressen aus
    /// <em>einer</em> Herkunft werden gebremst wie fünf mit derselben. Wäre die
    /// Adresse Teil des Schlüssels, käme jeder Versuch in einen eigenen Topf und
    /// niemand würde je gebremst — und die gebremste Antwort verriete außerdem,
    /// welche Adresse es gibt.
    /// </remarks>
    [Fact]
    public async Task Fuenf_verschiedene_Adressen_aus_einer_Herkunft_teilen_einen_Topf()
    {
        await using var wirt = Wirt(grenze: 3);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var antwort = await Anmelden(browser, "10.0.0.1", $"mensch{i}@example.org");
            gesehen.Add(antwort.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>Die Gegenrichtung: eine Herkunft sperrt keine andere aus.</summary>
    [Fact]
    public async Task Zwei_Herkuenfte_teilen_sich_keinen_Topf()
    {
        await using var wirt = Wirt(grenze: 2);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        for (var i = 0; i < 4; i++)
        {
            (await Anmelden(browser, "10.0.0.1")).Dispose();
        }

        using var andere = await Anmelden(browser, "10.0.0.2");

        andere.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "wer nichts getan hat, darf nicht ausgesperrt sein, weil ein anderer geraten hat");
    }

    [Fact]
    public async Task Ein_nicht_gelisteter_Pfad_wird_nie_gebremst()
    {
        await using var wirt = Wirt(grenze: 2);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        for (var i = 0; i < 20; i++)
        {
            using var antwort = await browser.GetAsync("/jobs");
            antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Die gebremste Antwort sagt nichts über ein Konto.
    /// </summary>
    /// <remarks>
    /// „zu viele Versuche für anna@…" wäre die Aufzählung durch die Hintertür.
    /// Die Bremse <em>kann</em> das gar nicht sagen, weil sie den Rumpf nie
    /// liest — dieser Test hält fest, dass das so bleibt.
    /// </remarks>
    [Fact]
    public async Task Die_Abweisung_nennt_kein_Konto()
    {
        await using var wirt = Wirt(grenze: 1);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1", "anna@example.org")).Dispose();
        using var abgewiesen = await Anmelden(browser, "10.0.0.1", "anna@example.org");

        var rumpf = await abgewiesen.Content.ReadAsStringAsync();

        rumpf.Should().NotContain("anna", "die Adresse darf in keiner Antwort auftauchen");
        rumpf.Should().NotContain("example.org");

        var gelesen = JsonDocument.Parse(rumpf).RootElement;
        gelesen.GetProperty("status").GetInt32().Should().Be(429);
        gelesen.GetProperty("detail").GetString().Should().Be("too many requests");
        gelesen.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        abgewiesen.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Die_Abweisung_sagt_wann_es_wieder_geht()
    {
        await using var wirt = Wirt(grenze: 1, fenster: TimeSpan.FromSeconds(30));
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1")).Dispose();
        using var abgewiesen = await Anmelden(browser, "10.0.0.1");

        abgewiesen.Headers.GetValues(Bremse.KopfSpaeter).Single().Should().Be("30");
        abgewiesen.Headers.GetValues(Bremse.KopfGrenze).Single().Should().Be("1");
        abgewiesen.Headers.GetValues(Bremse.KopfRest).Single().Should().Be("0");
    }

    [Fact]
    public async Task Das_Fenster_laeuft_ab_und_gibt_wieder_frei()
    {
        await using var wirt = Wirt(grenze: 1, fenster: TimeSpan.FromSeconds(1));
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1")).Dispose();
        (await Anmelden(browser, "10.0.0.1")).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        using var danach = await Anmelden(browser, "10.0.0.1");
        danach.StatusCode.Should().Be(
            HttpStatusCode.OK, "eine Bremse ist keine Sperre — sie muss wieder loslassen");
    }

    /// <summary>
    /// Ein mitgebrachter <c>X-Forwarded-For</c> verschiebt den Topf nicht.
    /// </summary>
    /// <remarks>
    /// <strong>Der Test, ohne den die Bremse eine Attrappe wäre.</strong> Diesen
    /// Kopf setzt der Aufrufer selbst. Würde die Herkunft daraus gelesen, drehte
    /// ein Angreifer ihn bei jeder Anfrage und wäre nie gebremst — die Bremse
    /// sähe von außen genauso aus und hielte nichts auf.
    /// <para>
    /// Vier Anfragen aus <em>einer</em> Verbindung mit vier <em>verschiedenen</em>
    /// erfundenen Weiterleitungsköpfen müssen sich also einen Topf teilen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Ein_mitgebrachter_Weiterleitungskopf_aendert_den_Topf_nicht()
    {
        await using var wirt = Wirt(grenze: 2);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var anfrage = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
            anfrage.Headers.TryAddWithoutValidation(HerkunftsKopf, "10.0.0.1");
            anfrage.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{i}");
            anfrage.Headers.TryAddWithoutValidation("X-Real-IP", $"198.51.100.{i}");

            using var antwort = await browser.SendAsync(anfrage);
            gesehen.Add(antwort.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// Die Gesundheitsprobe steht vor der Bremse — auch wenn sie in der Karte
    /// stünde.
    /// </summary>
    /// <remarks>
    /// Hier wird <c>/health/live</c> absichtlich MITGEBREMST und muss trotzdem
    /// vierzigmal antworten: <c>UseGesundheit()</c> steht vor <c>UseBremse()</c>
    /// und beendet die Kette vorher. Andersherum nähme eine gebremste Probe den
    /// Behälter aus dem Lastverteiler — die Bremse wäre selbst der Ausfall, und
    /// zwar genau unter Last.
    /// </remarks>
    [Fact]
    public async Task Die_Gesundheitsprobe_bleibt_ungebremst_selbst_wenn_sie_gelistet_ist()
    {
        await using var wirt = Wirt(grenze: 2, auchDieProbe: true);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        for (var i = 0; i < 40; i++)
        {
            using var antwort = await browser.GetAsync("/health/live");
            antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>Der Schlüssel besteht aus Pfad und Herkunft, sonst nichts.</summary>
    [Theory]
    [InlineData("/auth/login", "10.0.0.1", "bremse:/auth/login:10.0.0.1")]
    [InlineData("/auth/register", "::1", "bremse:/auth/register:::1")]
    public void Der_Schluessel_besteht_nur_aus_Pfad_und_Herkunft(
        string pfad, string herkunft, string erwartet) =>
        Bremse.Schluessel(pfad, herkunft).Should().Be(erwartet);

    /// <summary>
    /// Ein Rechner, zwei Schreibweisen, ein Topf.
    /// </summary>
    /// <remarks>
    /// Kestrel liefert eine IPv4-Adresse über einen Dual-Stack-Hörer als
    /// <c>::ffff:10.0.0.1</c>. Ohne diese Zusammenführung hätte derselbe
    /// Rechner zwei Töpfe und damit die doppelte Grenze — je nachdem, wie der
    /// Hörer konfiguriert ist.
    /// </remarks>
    [Fact]
    public void Dieselbe_Maschine_bekommt_nicht_zwei_Toepfe()
    {
        var schlicht = new DefaultHttpContext();
        schlicht.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        var verkleidet = new DefaultHttpContext();
        verkleidet.Connection.RemoteIpAddress =
            System.Net.IPAddress.Parse("10.0.0.1").MapToIPv6();

        Bremse.Herkunft(verkleidet).Should().Be(Bremse.Herkunft(schlicht));
    }

    /// <summary>
    /// Ohne feststellbare Herkunft wird nicht durchgewinkt.
    /// </summary>
    /// <remarks>
    /// Der Zweifelsfall muss <em>enger</em> ausfallen, nie weiter: wäre „keine
    /// Herkunft" der Freifahrtschein, wäre das Erzeugen dieses Zustands der
    /// erste Schritt jedes Angriffs.
    /// </remarks>
    [Fact]
    public void Ohne_Herkunft_gilt_ein_gemeinsamer_Name()
    {
        var ohne = new DefaultHttpContext();
        ohne.Connection.RemoteIpAddress = null;

        Bremse.Herkunft(ohne).Should().Be(Bremse.Namenlos);
    }

    /// <summary>Die Zahlen aus der ausgelieferten Karte, nicht aus einer Kopie.</summary>
    [Fact]
    public void Die_ausgelieferte_Karte_bremst_genau_die_fuenf_Auth_Pfade()
    {
        var karte = Bremskarte.Lesen();

        karte.Pfade.Keys.Should().BeEquivalentTo(
            "/auth/login",
            "/auth/register",
            "/auth/resend-verification",
            "/auth/verify-email",
            "/auth/refresh");

        karte.Fenster.Should().Be(TimeSpan.FromMinutes(1));
        karte.Pfade.Values.Should().OnlyContain(
            grenze => grenze > 0, "eine Grenze von null wäre eine Sperre, keine Bremse");
    }

    /// <summary>
    /// Die ausgelieferte Karte multipliziert nichts.
    /// </summary>
    /// <remarks>
    /// <c>Faktor</c> ist Luft für den Prüfstand und wird in
    /// <c>docker-compose.yml</c> gesetzt, nirgends sonst. Stünde er in der
    /// Karte, gälten die Zahlen daneben nicht mehr — und niemand würde es
    /// merken, weil die Bremse weiter antwortet, nur später.
    /// </remarks>
    [Fact]
    public void Die_ausgelieferte_Karte_multipliziert_nichts() =>
        Bremskarte.Lesen().Faktor.Should().Be(1);

    /// <summary>Ohne Angabe wird nicht multipliziert.</summary>
    [Fact]
    public void Der_Faktor_ist_ohne_Angabe_eins() =>
        new Bremseinstellungen().Faktor.Should().Be(1);

    /// <summary>
    /// Jeder gebremste Pfad ist auch eine Route.
    /// </summary>
    /// <remarks>
    /// Sonst bremst eine Zeile etwas, das es nicht gibt — und niemand merkt es,
    /// weil ein Tippfehler im Pfad genau so aussieht wie eine Bremse, die nie
    /// greift. Der umgekehrte Fall braucht keinen Test: eine Route ohne Bremse
    /// ist der Normalfall.
    /// </remarks>
    [Fact]
    public void Jeder_gebremste_Pfad_hat_eine_Route()
    {
        var karte = Bremskarte.Lesen();
        var routen = Bremskarte.Routen();

        foreach (var pfad in karte.Pfade.Keys)
        {
            routen.Should().Contain(
                route => Passt(route, pfad),
                $"'{pfad}' wird gebremst, aber keine Route führt dorthin");
        }
    }

    private static bool Passt(string route, string pfad)
    {
        if (string.Equals(route, pfad, StringComparison.Ordinal))
        {
            return true;
        }

        // `/auth/{rest}` deckt `/auth/login` ab.
        var klammer = route.IndexOf('{', StringComparison.Ordinal);

        return klammer > 0
               && pfad.StartsWith(route[..klammer], StringComparison.Ordinal)
               && pfad.Length > klammer;
    }
}

/// <summary>Liest die ausgelieferte <c>ocelot.json</c>.</summary>
internal static class Bremskarte
{
    private static JsonElement Wurzel()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "ocelot.json");

        return JsonDocument.Parse(
            File.ReadAllText(pfad),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;
    }

    public static Bremseinstellungen Lesen()
    {
        var abschnitt = Wurzel().GetProperty(Bremseinstellungen.Abschnitt);

        return new Bremseinstellungen
        {
            Fenster = TimeSpan.Parse(
                abschnitt.GetProperty("Fenster").GetString()!, CultureInfo.InvariantCulture),
            Faktor = abschnitt.TryGetProperty("Faktor", out var faktor) ? faktor.GetInt32() : 1,
            Pfade = abschnitt.GetProperty("Pfade").EnumerateObject()
                .ToDictionary(e => e.Name, e => e.Value.GetInt32(), StringComparer.Ordinal)
        };
    }

    public static IReadOnlyList<string> Routen() =>
        [.. Wurzel().GetProperty("Routes").EnumerateArray()
            .Select(r => r.GetProperty("UpstreamPathTemplate").GetString()!)];
}

/// <summary>
/// Die Bremse im ausgelieferten Gateway, nicht in einem Wirt für sie allein.
/// </summary>
/// <remarks>
/// Der Wirt oben prüft die Regel, diese Reihe prüft die <em>Verdrahtung</em>:
/// steht sie in <c>Program.cs</c> überhaupt in der Kette, liest sie die
/// ausgelieferte Karte, und liegt sie an der richtigen Stelle? Das ist eine
/// andere Frage, und beide sind schon einmal einzeln falsch gewesen.
/// <para>
/// Eigene Landschaft (<see cref="GebremsteLandschaft"/>), weil hier Grenzen
/// absichtlich ausgeschöpft werden.
/// </para>
/// </remarks>
[Collection(BremsSammlung.Name)]
public class GebremstesGatewayTests(GebremsteLandschaft landschaft)
{
    /// <summary>
    /// `/auth/resend-verification` ist mit 3 die engste Grenze der Karte — und
    /// sie ist die richtige dafür: der Endpunkt verschickt eine MAIL an eine
    /// Adresse, die dem Aufrufer nicht gehören muss.
    /// </summary>
    [Fact]
    public async Task Die_ausgelieferte_Kette_bremst_wirklich()
    {
        var gesehen = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var antwort = await landschaft.Browser.PostAsync(
                "/auth/resend-verification", content: null);
            gesehen.Add(antwort.StatusCode);
        }

        gesehen.Should().EndWith([HttpStatusCode.TooManyRequests]);
        gesehen.Count(s => s == HttpStatusCode.OK).Should().Be(3);
    }

    /// <summary>Ein gewöhnlicher Pfad bleibt ungebremst, auch oft aufgerufen.</summary>
    [Fact]
    public async Task Ein_Pfad_ohne_Eintrag_laeuft_durch()
    {
        for (var i = 0; i < 40; i++)
        {
            using var antwort = await landschaft.Browser.GetAsync("/jobs");
            antwort.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
