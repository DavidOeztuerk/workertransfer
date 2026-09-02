using System.Net;
using FluentAssertions;
using Girder.Abstractions.Caching;
using Girder.Abstractions.Hosting;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Auch die Antworten, die das Gateway SELBST schreibt, tragen Sicherheitsköpfe.
/// </summary>
/// <remarks>
/// <para><strong>Der Anlass, und die Korrektur daran.</strong> Ein Prüfbericht
/// meldete, Gateway-eigene Antworten trügen keine Sicherheitsköpfe, gemessen an
/// <c>GET /health/live</c>. Das stimmte — war aber am falschen Pfad gemessen:
/// Girders <c>ShouldSkipSecurityHeaders</c> überspringt alles, was
/// <c>/health</c>, <c>/metrics</c>, <c>/swagger</c> oder <c>/favicon</c> im Pfad
/// hat, und das ist eine Entscheidung, keine Lücke. Eine Gesundheitsprobe
/// beantwortet einen Lastverteiler, keinen Browser.</para>
///
/// <para><strong>Die echte Lücke war die 429 der Bremse.</strong> Sie ist eine
/// gewöhnliche Antwort an einen gewöhnlichen Aufrufer, sie trägt ein
/// Problemdokument, und sie kommt bei jemandem an, der nie einen Dienst
/// erreicht hat. Genau da fehlten die Köpfe — weil das Gateway
/// <c>AddWorkerTransferDefaults</c> bewusst nicht ruft und damit auch alles
/// Übrige verlor, was in dieser Kette steckt.</para>
///
/// <para>Diese Reihe prüft deshalb die 429 und nicht die Probe. Sie prüft
/// ausserdem die <strong>Stelle in der Kette</strong>: die Köpfe müssen auf
/// einer Antwort stehen, die schon in der Bremse endet und Ocelot nie erreicht.
/// Eine Stufe, die erst danach käme, wäre zu spät — und der Fehler sähe von
/// aussen genauso aus wie vorher.</para>
/// </remarks>
public sealed class SicherheitskoepfeTests
{
    private const string HerkunftsKopf = "X-Test-Herkunft";

    /// <summary>Der Wirt, in derselben Reihenfolge wie <c>Program.cs</c>.</summary>
    private static WebApplication Wirt(bool mitKoepfen)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();
        bau.Services.AddSingleton(new Bremseinstellungen
        {
            Fenster = TimeSpan.FromMinutes(1),
            Pfade = new Dictionary<string, int>(StringComparer.Ordinal) { ["/auth/login"] = 1 }
        });

        if (mitKoepfen)
        {
            bau.Services.AddGirder(
                bau.Configuration,
                bau.Environment,
                "gateway",
                girder => girder.Use(GirderModule.SecurityHeaders));
        }

        var wirt = bau.Build();

        // NUR IM TEST: die Herkunft setzen, damit die Bremse je Lauf einen
        // eigenen Topf hat und nicht die Schleife aller Tests teilt.
        wirt.Use(async (kontext, weiter) =>
        {
            if (kontext.Request.Headers.TryGetValue(HerkunftsKopf, out var wert))
            {
                kontext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(wert.ToString());
            }

            await weiter();
        });

        if (mitKoepfen)
        {
            wirt.UseSecurityHeaders();
        }

        wirt.UseBremse();

        return wirt;
    }

    private static HttpClient Browser(WebApplication wirt) =>
        new()
        {
            BaseAddress = new Uri(
                wirt.Urls.First(u => u.StartsWith("http://", StringComparison.Ordinal)))
        };

    /// <summary>Zweimal anmelden bei einer Grenze von eins — die zweite ist die 429.</summary>
    private static async Task<HttpResponseMessage> Abgewiesen(WebApplication wirt, string herkunft)
    {
        using var browser = Browser(wirt);
        browser.DefaultRequestHeaders.Add(HerkunftsKopf, herkunft);

        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        (await browser.PostAsync("/auth/login", content)).Dispose();

        using var zweiter = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await browser.PostAsync("/auth/login", zweiter);
    }

    /// <summary>Die Abweisung der Bremse trägt die Köpfe.</summary>
    [Fact]
    public async Task Die_Abweisung_der_Bremse_traegt_die_Koepfe()
    {
        await using var wirt = Wirt(mitKoepfen: true);
        await wirt.StartAsync();

        using var antwort = await Abgewiesen(wirt, "10.0.0.1");

        antwort.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        antwort.Headers.Contains("X-Content-Type-Options").Should().BeTrue(
            "ohne diesen Kopf darf der Browser den Inhalt nach Gutduenken raten");
        antwort.Headers.Contains("X-Frame-Options").Should().BeTrue(
            "sonst laesst sich die Antwort in einen fremden Rahmen setzen");
    }

    /// <summary>
    /// Die Gegenprobe: ohne die Stufe fehlen sie. Sonst prüfte diese Reihe
    /// etwas, das das Gerüst ohnehin mitbringt — und bliebe grün, wenn jemand
    /// die Stufe wieder herausnimmt.
    /// </summary>
    [Fact]
    public async Task Ohne_die_Stufe_fehlen_sie()
    {
        await using var wirt = Wirt(mitKoepfen: false);
        await wirt.StartAsync();

        using var antwort = await Abgewiesen(wirt, "10.0.0.2");

        antwort.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        antwort.Headers.Contains("X-Content-Type-Options").Should().BeFalse(
            "das Geruest setzt diesen Kopf NICHT von selbst — genau deshalb "
            + "braucht das Gateway die Stufe");
    }

    /// <summary>
    /// Und der Befund, der den ersten Bericht in die Irre führte: die
    /// Gesundheitsprobe bleibt ohne Köpfe, weil Girder <c>/health</c>
    /// ausdrücklich überspringt. Festgehalten, damit niemand es für einen
    /// Fehler hält und „repariert".
    /// </summary>
    [Fact]
    public async Task Die_Gesundheitsprobe_bleibt_bewusst_ohne_Koepfe()
    {
        await using var wirt = Wirt(mitKoepfen: true);
        wirt.UseGesundheit();
        await wirt.StartAsync();

        using var browser = Browser(wirt);
        using var antwort = await browser.GetAsync("/health/live");

        antwort.Headers.Contains("X-Content-Type-Options").Should().BeFalse(
            "Girders ShouldSkipSecurityHeaders ueberspringt /health — eine "
            + "Gesundheitsprobe beantwortet einen Lastverteiler, keinen Browser");
    }
}
