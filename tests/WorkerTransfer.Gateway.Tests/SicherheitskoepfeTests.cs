using System.Net;
using FluentAssertions;
using Noelia.Abstractions.Caching;
using Noelia.Infrastructure.Middleware;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Die Abweisung der Bremse trägt Sicherheitsköpfe — und sonst legt das
/// Gateway keine über fremde Responses.
/// </summary>
/// <remarks>
/// <para><strong>Beide Hälften sind teuer bezahlt.</strong> Ein Prüfbericht
/// meldete, Gateway-eigene Responses trügen keine Sicherheitsköpfe. Das stimmte
/// für die 429 der Bremse — sie ist eine gewöhnliche Antwort an einen
/// gewöhnlichen Aufrufer und kommt bei jemandem an, der nie einen Dienst
/// erreicht hat.</para>
///
/// <para><strong>Der erste Versuch, das zu beheben, machte die Oberfläche
/// kaputt.</strong> Noelias <c>UseSecurityHeaders()</c> vor die ganze Kette
/// gehängt legt seine Köpfe auch über alles <em>Durchgereichte</em> — und
/// bringt eine CSP mit <c>script-src 'self'</c> und
/// <c>upgrade-insecure-requests</c> mit. Gemessen im Browser: weisse Seite.
/// Vites Modul-Einstieg ist inline und seine Worker sind <c>blob:</c>, beides
/// von der CSP verboten; und <c>upgrade-insecure-requests</c> schrieb
/// <c>http://localhost:8090/…</c> auf <c>https://</c> um, wo niemand hört —
/// im Browserprotokoll als „TLS-Fehler" für <c>main.tsx</c> und
/// <c>config.js</c>.</para>
///
/// <para>Deshalb sitzen die Köpfe jetzt <strong>an der Abweisung selbst</strong>
/// und nicht als Stufe darüber. Das Gateway beantwortet fast nichts selbst; was
/// es durchreicht, gehört dem Dienst dahinter, und dessen Köpfe sind seine
/// Sache. Eine CSP ist für einen JSON-Rumpf ohnehin bedeutungslos.</para>
///
/// <para>Die zweite Prüfung unten ist die wichtigere: sie hält fest, dass eine
/// durchgereichte Antwort <em>keine</em> CSP vom Gateway bekommt. Ohne sie
/// könnte derselbe Fehler beim nächsten Aufräumen zurückkommen, und er sähe
/// von aussen aus wie eine Verbesserung.</para>
/// </remarks>
public sealed class SicherheitskoepfeTests
{
    private const string HerkunftsKopf = "X-Test-Herkunft";

    /// <summary>Der Wirt, in derselben Reihenfolge wie <c>Program.cs</c>.</summary>
    private static WebApplication Wirt()
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
        bau.Services.Configure<DistributedRateLimitingOptions>(einstellungen =>
        {
            einstellungen.RequestsPerMinute = 0;
            einstellungen.RequestsPerHour = 0;
            einstellungen.RequestsPerDay = 0;
            einstellungen.Subject = RateLimitSubject.Origin;
            einstellungen.WhitelistedIps = [];
            einstellungen.WhitelistedEndpoints = [];
            einstellungen.EndpointSpecificLimits = new Dictionary<string, EndpointRateLimit>(
                StringComparer.Ordinal)
            {
                ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = 1 }
            };
        });

        var wirt = bau.Build();

        wirt.Use(async (context, weiter) =>
        {
            if (context.Request.Headers.TryGetValue(HerkunftsKopf, out var value))
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(value.ToString());
            }

            await weiter();
        });

        wirt.UseMiddleware<DistributedRateLimitingMiddleware>();

        // Steht für das, was Ocelot sonst durchreicht: eine fremde Seite, die
        // ihre eigenen Köpfe mitbringt (oder eben keine).
        wirt.Run(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<!doctype html><p>durchgereicht");
        });

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

        using var erster = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        (await browser.PostAsync("/auth/login", erster)).Dispose();

        using var zweiter = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await browser.PostAsync("/auth/login", zweiter);
    }

    /// <summary>Die Abweisung trägt die zwei Köpfe, die für sie zählen.</summary>
    [Fact]
    public async Task Die_Abweisung_traegt_die_Koepfe()
    {
        await using var wirt = Wirt();
        await wirt.StartAsync();

        using var response = await Abgewiesen(wirt, "10.0.0.1");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
    }

    /// <summary>
    /// Eine durchgereichte Antwort bekommt vom Gateway KEINE CSP — sonst ist
    /// die Oberfläche weiss.
    /// </summary>
    [Fact]
    public async Task Durchgereichtes_bekommt_keine_CSP_vom_Gateway()
    {
        await using var wirt = Wirt();
        await wirt.StartAsync();

        using var browser = Browser(wirt);
        using var response = await browser.GetAsync("/verify");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.Contains("Content-Security-Policy").Should().BeFalse(
            "eine CSP des Gateways ueber fremdem HTML verbietet Vites inline-Einstieg "
            + "und seine blob:-Worker — die Seite bleibt weiss");

        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        (values ?? []).Should().NotContain(
            value => value.Contains("upgrade-insecure-requests", StringComparison.Ordinal),
            "der Browser schriebe http://localhost:8090 auf https:// um, wo niemand hoert");
    }
}
