using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Girder.Abstractions.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.RateLimiting;
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

    private static WebApplication Wirt(int grenze, bool auchDieProbe = false)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();

        var grenzen = new Dictionary<string, EndpointRateLimit>(StringComparer.Ordinal)
        {
            ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = grenze }
        };

        if (auchDieProbe)
        {
            grenzen["/health/live"] = new EndpointRateLimit { RequestsPerMinute = grenze };
        }

        bau.Services.Configure<DistributedRateLimitingOptions>(einstellungen =>
        {
            // Wie in der ausgelieferten Karte: keine Vorgabe ueber allem, damit
            // NUR die genannten Pfade zaehlen.
            einstellungen.RequestsPerMinute = 0;
            einstellungen.RequestsPerHour = 0;
            einstellungen.RequestsPerDay = 0;
            einstellungen.Subject = RateLimitSubject.Origin;

            // Loopback steht per Vorgabe auf der Ausnahmeliste, und im Test kommt
            // alles von dort — ohne diese Zeile misst die Probe eine Bremse, die
            // gar nicht zaehlt.
            einstellungen.WhitelistedIps = [];
            einstellungen.WhitelistedEndpoints = [];
            einstellungen.EndpointSpecificLimits = grenzen;
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
        wirt.UseMiddleware<CorrelationIdMiddleware>();
        wirt.UseMiddleware<DistributedRateLimitingMiddleware>();
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
        // Der Wortlaut ist Girders. Was hier zaehlt, ist die Zusage darueber:
        // er nennt kein Konto — und kann es nicht, weil die Bremse den Rumpf
        // nie liest.
        gelesen.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        gelesen.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        abgewiesen.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Die_Abweisung_sagt_wann_es_wieder_geht()
    {
        await using var wirt = Wirt(grenze: 1);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1")).Dispose();
        using var abgewiesen = await Anmelden(browser, "10.0.0.1");

        // Eine Minute, und die ist nicht mehr verstellbar: Girder zaehlt in
        // festen Fenstern (Minute, Stunde, Tag). Die ausgelieferte Karte stand
        // ohnehin auf einer Minute, also aendert sich im Betrieb nichts.
        abgewiesen.Headers.GetValues("Retry-After").Single().Should().Be("60");
        abgewiesen.Headers.GetValues("X-RateLimit-Limit").Single().Should().Be("1");
        abgewiesen.Headers.GetValues("X-RateLimit-Remaining").Single().Should().Be("0");
    }

    // `Das_Fenster_laeuft_ab_und_gibt_wieder_frei` stand hier und ist gegangen.
    // Es fuhr ein Ein-Sekunden-Fenster und wartete 1,5 Sekunden; Girder zaehlt in
    // festen Fenstern, also hiesse derselbe Test jetzt eine Minute warten.
    //
    // Die Zusage — eine Bremse ist keine Sperre, sie muss wieder loslassen —
    // haelt Girders `RateLimitStoreConformance`, an einem kurzen Fenster und
    // gegen JEDEN Speicher, auch den von Redis. Dort gehoert sie hin: sie ist
    // eine Eigenschaft des Zaehlers, nicht unserer Verdrahtung.

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

    // Drei Tests standen hier und sind mit `Bremse.cs` gegangen: die Gestalt des
    // Zählerschlüssels, die Zusammenführung von `::ffff:10.0.0.1`, und der
    // gemeinsame Topf ohne feststellbare Herkunft. Alle drei prüften eine
    // Umsetzung, die es hier nicht mehr gibt — sie gehören jetzt zu
    // `ClientAddress` in Girder und werden dort geprüft.
    //
    // Die ZUSAGE, die sie trugen, steht weiter oben und stärker: „verschiedene
    // Adressen aus einer Herkunft teilen einen Topf" fährt fünf echte Anmeldungen
    // durch das echte Gateway. Ein Schlüssel, der die Adresse enthielte, fiele
    // dort — ohne dass der Test seine Gestalt kennen müsste.

    /// <summary>Die Zahlen aus der ausgelieferten Karte, nicht aus einer Kopie.</summary>
    [Fact]
    public void Die_ausgelieferte_Karte_bremst_genau_die_fuenf_Auth_Pfade()
    {
        var pfade = Bremskarte.Pfade();

        pfade.Keys.Should().BeEquivalentTo(
            "/auth/login",
            "/auth/register",
            "/auth/resend-verification",
            "/auth/verify-email",
            "/auth/refresh");

        pfade.Values.Should().OnlyContain(
            grenze => grenze > 0, "eine Grenze von null wäre eine Sperre, keine Bremse");
    }

    /// <summary>
    /// Die drei Vorgaben stehen auf 0 — und daran hängt alles.
    /// </summary>
    /// <remarks>
    /// Eine Vorgabe legt einen Zähler für JEDEN Pfad an. Durch dieses Gateway
    /// läuft auch die ganze Oberfläche, also zählte dann jeder Bildabruf mit,
    /// und die fünf Auth-Grenzen wären nur noch die zusätzliche Verschärfung
    /// darüber. Genau diese Form war der Grund, warum hier jahrelang eine eigene
    /// Bremse stand — bis jemand nachgemessen hat, dass 0 sie abschaltet.
    /// </remarks>
    [Fact]
    public void Die_ausgelieferte_Karte_hat_keine_Vorgabe_ueber_allem() =>
        Bremskarte.Vorgaben().Should().AllSatisfy(vorgabe => vorgabe.Should().Be(0));

    /// <summary>
    /// Die ausgelieferte Karte multipliziert nichts.
    /// </summary>
    /// <remarks>
    /// <c>LimitMultiplier</c> ist Luft für den Prüfstand und wird in
    /// <c>docker-compose.yml</c> gesetzt, nirgends sonst. Stünde er in der
    /// Karte, gälten die Zahlen daneben nicht mehr — und niemand würde es
    /// merken, weil die Bremse weiter antwortet, nur später.
    /// </remarks>
    [Fact]
    public void Die_ausgelieferte_Karte_multipliziert_nichts() =>
        Bremskarte.Multiplikator().Should().Be(1);

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
        var pfade = Bremskarte.Pfade();
        var routen = Bremskarte.Routen();

        foreach (var pfad in pfade.Keys)
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

    /// <summary>
    /// Die gebremsten Pfade und ihre Grenzen, aus Girders Abschnitt.
    /// </summary>
    /// <remarks>
    /// Gelesen statt gebaut: was hier steht, ist genau das, was der Behälter
    /// bindet. Ein Test gegen eine eigene Kopie prüfte seine eigene Kopie.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Pfade()
    {
        var abschnitt = Wurzel().GetProperty(DistributedRateLimitingOptions.SectionName);

        return abschnitt.GetProperty("EndpointSpecificLimits").EnumerateObject()
            .ToDictionary(
                eintrag => eintrag.Name,
                eintrag => eintrag.Value.GetProperty("RequestsPerMinute").GetInt32(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Die drei Vorgaben. Sie MÜSSEN 0 sein — sonst zählt jeder Pfad mit.
    /// </summary>
    public static IReadOnlyList<int> Vorgaben()
    {
        var abschnitt = Wurzel().GetProperty(DistributedRateLimitingOptions.SectionName);

        return
        [
            abschnitt.GetProperty("RequestsPerMinute").GetInt32(),
            abschnitt.GetProperty("RequestsPerHour").GetInt32(),
            abschnitt.GetProperty("RequestsPerDay").GetInt32()
        ];
    }

    /// <summary>Der Multiplikator, sofern die Karte einen nennt.</summary>
    public static int Multiplikator()
    {
        var abschnitt = Wurzel().GetProperty(DistributedRateLimitingOptions.SectionName);

        return abschnitt.TryGetProperty("LimitMultiplier", out var wert) ? wert.GetInt32() : 1;
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
