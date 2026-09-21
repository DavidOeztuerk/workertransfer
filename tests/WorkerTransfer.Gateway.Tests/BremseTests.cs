using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Noelia.Abstractions.Caching;
using Noelia.Infrastructure.Middleware;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// The rate limiter itself, without Ocelot: what counts and what does not.
/// </summary>
/// <remarks>
/// A small host of its own instead of the whole landscape, for a reason that is
/// the actual point here: <strong>the origin has to be adjustable.</strong> Over
/// a real socket every call comes from <c>127.0.0.1</c>, and then it could never
/// be shown that two origins have separate buckets — the test would be green
/// even if the brake threw all people into one bucket. That is exactly the bad
/// defect.
/// </remarks>
public sealed class BremseTests
{
    private const string HerkunftsKopf = "X-Test-Herkunft";

    private static WebApplication Wirt(int limit, bool auchDieProbe = false)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();

        var limits = new Dictionary<string, EndpointRateLimit>(StringComparer.Ordinal)
        {
            ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = limit }
        };

        if (auchDieProbe)
        {
            limits["/health/live"] = new EndpointRateLimit { RequestsPerMinute = limit };
        }

        bau.Services.Configure<DistributedRateLimitingOptions>(einstellungen =>
        {
            // As in the shipped map: no default over everything, so that ONLY
            // the named paths count.
            einstellungen.RequestsPerMinute = 0;
            einstellungen.RequestsPerHour = 0;
            einstellungen.RequestsPerDay = 0;
            einstellungen.Subject = RateLimitSubject.Origin;

            // Loopback stands on the exemption list by default, and in the test
            // everything comes from there — without this line the probe measures
            // a brake that does not count at all.
            einstellungen.WhitelistedIps = [];
            einstellungen.WhitelistedEndpoints = [];
            einstellungen.EndpointSpecificLimits = limits;
        });

        var wirt = bau.Build();

        // ONLY IN THE TEST: set the connection's origin. The generator takes it
        // out of `Connection.RemoteIpAddress`, and over a real socket that is
        // always the same loopback. This stands BEFORE the brake, so that it
        // reads what was set here.
        wirt.Use(async (context, weiter) =>
        {
            if (context.Request.Headers.TryGetValue(HerkunftsKopf, out var value))
            {
                context.Connection.RemoteIpAddress =
                    System.Net.IPAddress.Parse(value.ToString());
            }

            await weiter();
        });

        // The same order as in `Program.cs`: the probe first.
        wirt.UseGesundheit();
        wirt.UseMiddleware<CorrelationIdMiddleware>();
        wirt.UseMiddleware<DistributedRateLimitingMiddleware>();
        wirt.Map("/{**alles}", () => Results.Ok(new { durch = true }));

        return wirt;
    }

    private static async Task<HttpResponseMessage> Anmelden(
        HttpClient browser, string herkunft, string address = "anna@example.org")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = address, password = "geheim" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation(HerkunftsKopf, herkunft);

        return await browser.SendAsync(request);
    }

    private static HttpClient Browser(WebApplication wirt) =>
        new() { BaseAddress = new Uri(wirt.Urls.First()) };

    [Fact]
    public async Task Above_the_limit_a_429_comes()
    {
        await using var wirt = Wirt(limit: 3);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await Anmelden(browser, "10.0.0.1");
            gesehen.Add(response.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// The promise it is all about: <strong>per origin, never per address.</strong>
    /// </summary>
    /// <remarks>
    /// Five sign-in attempts with five <em>different</em> addresses from
    /// <em>one</em> origin are braked like five with the same one. If the
    /// address were part of the key, every attempt would land in a bucket of its
    /// own and nobody would ever be braked — and the braked answer would on top
    /// of that reveal which address exists.
    /// </remarks>
    [Fact]
    public async Task Five_different_addresses_from_one_origin_share_a_bucket()
    {
        await using var wirt = Wirt(limit: 3);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await Anmelden(browser, "10.0.0.1", $"mensch{i}@example.org");
            gesehen.Add(response.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>The reverse direction: one origin locks out no other.</summary>
    [Fact]
    public async Task Two_origins_do_not_share_a_bucket()
    {
        await using var wirt = Wirt(limit: 2);
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
    public async Task A_path_that_is_not_listed_is_never_braked()
    {
        await using var wirt = Wirt(limit: 2);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        for (var i = 0; i < 20; i++)
        {
            using var response = await browser.GetAsync("/jobs");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// The braked answer says nothing about an account.
    /// </summary>
    /// <remarks>
    /// "too many attempts for anna@…" would be enumeration through the back
    /// door. The brake <em>cannot</em> say that at all, because it never reads
    /// the body — this test pins that it stays that way.
    /// </remarks>
    [Fact]
    public async Task The_rejection_names_no_account()
    {
        await using var wirt = Wirt(limit: 1);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1", "anna@example.org")).Dispose();
        using var abgewiesen = await Anmelden(browser, "10.0.0.1", "anna@example.org");

        var body = await abgewiesen.Content.ReadAsStringAsync();

        body.Should().NotContain("anna", "die Adresse darf in keiner Antwort auftauchen");
        body.Should().NotContain("example.org");

        var gelesen = JsonDocument.Parse(body).RootElement;
        gelesen.GetProperty("status").GetInt32().Should().Be(429);
        // The wording is Noelia's. What counts here is the promise above it:
        // it names no account — and cannot, because the brake never reads the
        // body.
        gelesen.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        gelesen.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        abgewiesen.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task The_rejection_says_when_it_works_again()
    {
        await using var wirt = Wirt(limit: 1);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        (await Anmelden(browser, "10.0.0.1")).Dispose();
        using var abgewiesen = await Anmelden(browser, "10.0.0.1");

        // One minute, and that is no longer adjustable: Noelia counts in fixed
        // windows (minute, hour, day). The shipped map stood at one minute
        // anyway, so nothing changes in operation.
        abgewiesen.Headers.GetValues("Retry-After").Single().Should().Be("60");
        abgewiesen.Headers.GetValues("X-RateLimit-Limit").Single().Should().Be("1");
        abgewiesen.Headers.GetValues("X-RateLimit-Remaining").Single().Should().Be("0");
    }

    // `Das_Fenster_laeuft_ab_und_gibt_wieder_frei` stood here and has gone. It
    // ran a one-second window and waited 1.5 seconds; Noelia counts in fixed
    // windows, so the same test would now mean waiting a minute.
    //
    // The promise — a brake is not a lock, it has to let go again — is held by
    // Noelia's `RateLimitStoreConformance`, on a short window and against EVERY
    // store, including Redis's. That is where it belongs: it is a property of
    // the counter, not of our wiring.

    /// <summary>
    /// An <c>X-Forwarded-For</c> brought along does not move the bucket.
    /// </summary>
    /// <remarks>
    /// <strong>The test without which the brake would be a dummy.</strong> This
    /// header is set by the caller themselves. If the origin were read out of
    /// it, an attacker would turn it on every request and would never be braked
    /// — the brake would look the same from outside and would stop nothing.
    /// <para>
    /// Four requests from <em>one</em> connection with four <em>different</em>
    /// invented forwarding headers therefore have to share one bucket.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_forwarding_header_brought_along_does_not_change_the_bucket()
    {
        await using var wirt = Wirt(limit: 2);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        var gesehen = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
            request.Headers.TryAddWithoutValidation(HerkunftsKopf, "10.0.0.1");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{i}");
            request.Headers.TryAddWithoutValidation("X-Real-IP", $"198.51.100.{i}");

            using var response = await browser.SendAsync(request);
            gesehen.Add(response.StatusCode);
        }

        gesehen.Should().Equal(
            HttpStatusCode.OK,
            HttpStatusCode.OK,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// The health probe stands before the brake — even if it stood in the map.
    /// </summary>
    /// <remarks>
    /// Here <c>/health/live</c> is deliberately BRAKED ALONG and must answer
    /// forty times all the same: <c>UseGesundheit()</c> stands before
    /// <c>UseBremse()</c> and ends the chain earlier. The other way round a
    /// braked probe would take the container out of the load balancer — the
    /// brake would itself be the outage, and precisely under load.
    /// </remarks>
    [Fact]
    public async Task The_health_probe_stays_unbraked_even_when_it_is_listed()
    {
        await using var wirt = Wirt(limit: 2, auchDieProbe: true);
        await wirt.StartAsync();
        using var browser = Browser(wirt);

        for (var i = 0; i < 40; i++)
        {
            using var response = await browser.GetAsync("/health/live");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // Three tests stood here and went with `Bremse.cs`: the shape of the
    // counter key, the merging of `::ffff:10.0.0.1`, and the shared bucket
    // without a determinable origin. All three checked an implementation that
    // no longer exists here — they belong to `ClientAddress` in Noelia now and
    // are checked there.
    //
    // The PROMISE they carried stands further up and stronger: "different
    // addresses from one origin share a bucket" drives five real sign-ins
    // through the real gateway. A key that contained the address would fall
    // there — without the test having to know its shape.

    /// <summary>The numbers out of the shipped map, not out of a copy.</summary>
    [Fact]
    public void The_shipped_map_brakes_exactly_the_five_auth_paths()
    {
        var pfade = Bremskarte.Pfade();

        pfade.Keys.Should().BeEquivalentTo(
            "/auth/login",
            "/auth/register",
            "/auth/resend-verification",
            "/auth/verify-email",
            "/auth/refresh");

        pfade.Values.Should().OnlyContain(
            limit => limit > 0, "eine Grenze von null wäre eine Sperre, keine Bremse");
    }

    /// <summary>
    /// The three defaults stand at 0 — and everything hangs on that.
    /// </summary>
    /// <remarks>
    /// A default writes a counter for EVERY path. The whole interface travels
    /// through this gateway too, so every asset fetch would then count along,
    /// and the five auth limits would only be the additional tightening above
    /// it. Exactly this shape was the reason a hand-written brake stood here for
    /// years — until somebody measured that 0 switches it off.
    /// </remarks>
    [Fact]
    public void The_shipped_map_has_no_default_over_everything() =>
        Bremskarte.Vorgaben().Should().AllSatisfy(defaultValue => defaultValue.Should().Be(0));

    /// <summary>
    /// The shipped map multiplies nothing.
    /// </summary>
    /// <remarks>
    /// <c>LimitMultiplier</c> is headroom for the test bench and is set in
    /// <c>docker-compose.yml</c>, nowhere else. If it stood in the map, the
    /// numbers beside it would no longer apply — and nobody would notice,
    /// because the brake goes on answering, only later.
    /// </remarks>
    [Fact]
    public void The_shipped_map_multiplies_nothing() =>
        Bremskarte.Multiplikator().Should().Be(1);

    /// <summary>
    /// Every braked path is also a route.
    /// </summary>
    /// <remarks>
    /// Otherwise a line brakes something that does not exist — and nobody
    /// notices, because a typo in the path looks exactly like a brake that never
    /// bites. The reverse case needs no test: a route without a brake is the
    /// normal case.
    /// </remarks>
    [Fact]
    public void Every_braked_path_has_a_route()
    {
        var pfade = Bremskarte.Pfade();
        var routen = Bremskarte.Routen();

        foreach (var path in pfade.Keys)
        {
            routen.Should().Contain(
                route => Passt(route, path),
                $"'{path}' wird gebremst, aber keine Route führt dorthin");
        }
    }

    private static bool Passt(string route, string path)
    {
        if (string.Equals(route, path, StringComparison.Ordinal))
        {
            return true;
        }

        // `/auth/{rest}` covers `/auth/login`.
        var klammer = route.IndexOf('{', StringComparison.Ordinal);

        return klammer > 0
               && path.StartsWith(route[..klammer], StringComparison.Ordinal)
               && path.Length > klammer;
    }
}

/// <summary>Reads the shipped <c>ocelot.json</c>.</summary>
internal static class Bremskarte
{
    private static JsonElement Wurzel()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ocelot.json");

        return JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;
    }

    /// <summary>
    /// The braked paths and their limits, out of Noelia's section.
    /// </summary>
    /// <remarks>
    /// Read rather than built: what stands here is exactly what the container
    /// binds. A test against a copy of its own would check its own copy.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Pfade()
    {
        var abschnitt = Wurzel().GetProperty(DistributedRateLimitingOptions.SectionName);

        return abschnitt.GetProperty("EndpointSpecificLimits").EnumerateObject()
            .ToDictionary(
                entry => entry.Name,
                entry => entry.Value.GetProperty("RequestsPerMinute").GetInt32(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The three defaults. They MUST be 0 — otherwise every path counts along.
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

    /// <summary>The multiplier, if the map names one.</summary>
    public static int Multiplikator()
    {
        var abschnitt = Wurzel().GetProperty(DistributedRateLimitingOptions.SectionName);

        return abschnitt.TryGetProperty("LimitMultiplier", out var value) ? value.GetInt32() : 1;
    }

    public static IReadOnlyList<string> Routen() =>
        [.. Wurzel().GetProperty("Routes").EnumerateArray()
            .Select(r => r.GetProperty("UpstreamPathTemplate").GetString()!)];
}

/// <summary>
/// The brake in the shipped gateway, not in a host of its own.
/// </summary>
/// <remarks>
/// The host above checks the rule, this suite checks the <em>wiring</em>: does
/// it stand in the chain in <c>Program.cs</c> at all, does it read the shipped
/// map, and does it lie in the right place? That is a different question, and
/// both have been wrong individually at one time or another.
/// <para>
/// A landscape of its own (<see cref="GebremsteLandschaft"/>), because limits
/// are deliberately exhausted here.
/// </para>
/// </remarks>
[Collection(BremsSammlung.Name)]
public class GebremstesGatewayTests(GebremsteLandschaft landschaft)
{
    /// <summary>
    /// At 3, `/auth/resend-verification` is the tightest limit of the map — and
    /// it is the right one for it: the endpoint sends a MAIL to an address that
    /// need not belong to the caller.
    /// </summary>
    [Fact]
    public async Task The_shipped_chain_really_brakes()
    {
        var gesehen = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var response = await landschaft.Browser.PostAsync(
                "/auth/resend-verification", content: null);
            gesehen.Add(response.StatusCode);
        }

        gesehen.Should().EndWith([HttpStatusCode.TooManyRequests]);
        gesehen.Count(s => s == HttpStatusCode.OK).Should().Be(3);
    }

    /// <summary>An ordinary path stays unbraked, even when called often.</summary>
    [Fact]
    public async Task A_path_without_an_entry_runs_through()
    {
        for (var i = 0; i < 40; i++)
        {
            using var response = await landschaft.Browser.GetAsync("/jobs");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
