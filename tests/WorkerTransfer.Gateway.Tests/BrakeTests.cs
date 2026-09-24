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
public sealed class BrakeTests
{
    private const string OriginHeader = "X-Test-Origin";

    private static WebApplication BuildHost(int limit, bool alsoTheProbe = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();

        var limits = new Dictionary<string, EndpointRateLimit>(StringComparer.Ordinal)
        {
            ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = limit }
        };

        if (alsoTheProbe)
        {
            limits["/health/live"] = new EndpointRateLimit { RequestsPerMinute = limit };
        }

        builder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            // As in the shipped map: no default over everything, so that ONLY
            // the named paths count.
            options.RequestsPerMinute = 0;
            options.RequestsPerHour = 0;
            options.RequestsPerDay = 0;
            options.Subject = RateLimitSubject.Origin;

            // Loopback stands on the exemption list by default, and in the test
            // everything comes from there — without this line the probe measures
            // a brake that does not count at all.
            options.WhitelistedIps = [];
            options.WhitelistedEndpoints = [];
            options.EndpointSpecificLimits = limits;
        });

        var host = builder.Build();

        // ONLY IN THE TEST: set the connection's origin. The generator takes it
        // out of `Connection.RemoteIpAddress`, and over a real socket that is
        // always the same loopback. This stands BEFORE the brake, so that it
        // reads what was set here.
        host.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(OriginHeader, out var value))
            {
                context.Connection.RemoteIpAddress =
                    System.Net.IPAddress.Parse(value.ToString());
            }

            await next();
        });

        // The same order as in `Program.cs`: the probe first.
        host.UseHealthProbes();
        host.UseMiddleware<CorrelationIdMiddleware>();
        host.UseMiddleware<DistributedRateLimitingMiddleware>();
        host.Map("/{**rest}", () => Results.Ok(new { through = true }));

        return host;
    }

    private static async Task<HttpResponseMessage> SignIn(
        HttpClient browser, string origin, string address = "anna@example.org")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { email = address, password = "secret" }),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.TryAddWithoutValidation(OriginHeader, origin);

        return await browser.SendAsync(request);
    }

    private static HttpClient Browser(WebApplication host) =>
        new() { BaseAddress = new Uri(host.Urls.First()) };

    [Fact]
    public async Task Above_the_limit_a_429_comes()
    {
        await using var host = BuildHost(limit: 3);
        await host.StartAsync();
        using var browser = Browser(host);

        var seen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await SignIn(browser, "10.0.0.1");
            seen.Add(response.StatusCode);
        }

        seen.Should().Equal(
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
        await using var host = BuildHost(limit: 3);
        await host.StartAsync();
        using var browser = Browser(host);

        var seen = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await SignIn(browser, "10.0.0.1", $"person{i}@example.org");
            seen.Add(response.StatusCode);
        }

        seen.Should().Equal(
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
        await using var host = BuildHost(limit: 2);
        await host.StartAsync();
        using var browser = Browser(host);

        for (var i = 0; i < 4; i++)
        {
            (await SignIn(browser, "10.0.0.1")).Dispose();
        }

        using var other = await SignIn(browser, "10.0.0.2");

        other.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "whoever did nothing must not be locked out because somebody else guessed");
    }

    [Fact]
    public async Task A_path_that_is_not_listed_is_never_braked()
    {
        await using var host = BuildHost(limit: 2);
        await host.StartAsync();
        using var browser = Browser(host);

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
        await using var host = BuildHost(limit: 1);
        await host.StartAsync();
        using var browser = Browser(host);

        (await SignIn(browser, "10.0.0.1", "anna@example.org")).Dispose();
        using var rejected = await SignIn(browser, "10.0.0.1", "anna@example.org");

        var body = await rejected.Content.ReadAsStringAsync();

        body.Should().NotContain("anna", "the address must appear in no answer");
        body.Should().NotContain("example.org");

        var parsed = JsonDocument.Parse(body).RootElement;
        parsed.GetProperty("status").GetInt32().Should().Be(429);
        // The wording is Noelia's. What counts here is the promise above it:
        // it names no account — and cannot, because the brake never reads the
        // body.
        parsed.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        parsed.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        rejected.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");
    }

    [Fact]
    public async Task The_rejection_says_when_it_works_again()
    {
        await using var host = BuildHost(limit: 1);
        await host.StartAsync();
        using var browser = Browser(host);

        (await SignIn(browser, "10.0.0.1")).Dispose();
        using var rejected = await SignIn(browser, "10.0.0.1");

        // One minute, and that is no longer adjustable: Noelia counts in fixed
        // windows (minute, hour, day). The shipped map stood at one minute
        // anyway, so nothing changes in operation.
        rejected.Headers.GetValues("Retry-After").Single().Should().Be("60");
        rejected.Headers.GetValues("X-RateLimit-Limit").Single().Should().Be("1");
        rejected.Headers.GetValues("X-RateLimit-Remaining").Single().Should().Be("0");
    }

    // A test that the window expires and lets go again stood here and has gone. It
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
        await using var host = BuildHost(limit: 2);
        await host.StartAsync();
        using var browser = Browser(host);

        var seen = new List<HttpStatusCode>();

        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login");
            request.Headers.TryAddWithoutValidation(OriginHeader, "10.0.0.1");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", $"203.0.113.{i}");
            request.Headers.TryAddWithoutValidation("X-Real-IP", $"198.51.100.{i}");

            using var response = await browser.SendAsync(request);
            seen.Add(response.StatusCode);
        }

        seen.Should().Equal(
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
    /// forty times all the same: <c>UseHealthProbes()</c> stands before
    /// the brake and ends the chain earlier. The other way round a
    /// braked probe would take the container out of the load balancer — the
    /// brake would itself be the outage, and precisely under load.
    /// </remarks>
    [Fact]
    public async Task The_health_probe_stays_unbraked_even_when_it_is_listed()
    {
        await using var host = BuildHost(limit: 2, alsoTheProbe: true);
        await host.StartAsync();
        using var browser = Browser(host);

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
        var paths = BrakeMap.Paths();

        paths.Keys.Should().BeEquivalentTo(
            "/auth/login",
            "/auth/register",
            "/auth/resend-verification",
            "/auth/verify-email",
            "/auth/refresh");

        paths.Values.Should().OnlyContain(
            limit => limit > 0, "a limit of zero would be a lock, not a brake");
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
        BrakeMap.Defaults().Should().AllSatisfy(defaultValue => defaultValue.Should().Be(0));

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
        BrakeMap.Multiplier().Should().Be(1);

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
        var paths = BrakeMap.Paths();
        var routes = BrakeMap.Routes();

        foreach (var path in paths.Keys)
        {
            routes.Should().Contain(
                route => Matches(route, path),
                $"'{path}' is braked, but no route leads there");
        }
    }

    private static bool Matches(string route, string path)
    {
        if (string.Equals(route, path, StringComparison.Ordinal))
        {
            return true;
        }

        // `/auth/{rest}` covers `/auth/login`.
        var brace = route.IndexOf('{', StringComparison.Ordinal);

        return brace > 0
               && path.StartsWith(route[..brace], StringComparison.Ordinal)
               && path.Length > brace;
    }
}

/// <summary>Reads the shipped <c>ocelot.json</c>.</summary>
internal static class BrakeMap
{
    private static JsonElement Root()
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
    public static IReadOnlyDictionary<string, int> Paths()
    {
        var section = Root().GetProperty(DistributedRateLimitingOptions.SectionName);

        return section.GetProperty("EndpointSpecificLimits").EnumerateObject()
            .ToDictionary(
                entry => entry.Name,
                entry => entry.Value.GetProperty("RequestsPerMinute").GetInt32(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The three defaults. They MUST be 0 — otherwise every path counts along.
    /// </summary>
    public static IReadOnlyList<int> Defaults()
    {
        var section = Root().GetProperty(DistributedRateLimitingOptions.SectionName);

        return
        [
            section.GetProperty("RequestsPerMinute").GetInt32(),
            section.GetProperty("RequestsPerHour").GetInt32(),
            section.GetProperty("RequestsPerDay").GetInt32()
        ];
    }

    /// <summary>The multiplier, if the map names one.</summary>
    public static int Multiplier()
    {
        var section = Root().GetProperty(DistributedRateLimitingOptions.SectionName);

        return section.TryGetProperty("LimitMultiplier", out var value) ? value.GetInt32() : 1;
    }

    public static IReadOnlyList<string> Routes() =>
        [.. Root().GetProperty("Routes").EnumerateArray()
            .Select(route => route.GetProperty("UpstreamPathTemplate").GetString()!)];
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
/// A landscape of its own (<see cref="BrakedLandscape"/>), because limits
/// are deliberately exhausted here.
/// </para>
/// </remarks>
[Collection(BrakeCollection.Name)]
public class BrakedGatewayTests(BrakedLandscape landscape)
{
    /// <summary>
    /// At 3, `/auth/resend-verification` is the tightest limit of the map — and
    /// it is the right one for it: the endpoint sends a MAIL to an address that
    /// need not belong to the caller.
    /// </summary>
    [Fact]
    public async Task The_shipped_chain_really_brakes()
    {
        var seen = new List<HttpStatusCode>();

        for (var i = 0; i < 5; i++)
        {
            using var response = await landscape.Browser.PostAsync(
                "/auth/resend-verification", content: null);
            seen.Add(response.StatusCode);
        }

        seen.Should().EndWith([HttpStatusCode.TooManyRequests]);
        seen.Count(status => status == HttpStatusCode.OK).Should().Be(3);
    }

    /// <summary>An ordinary path stays unbraked, even when called often.</summary>
    [Fact]
    public async Task A_path_without_an_entry_runs_through()
    {
        for (var i = 0; i < 40; i++)
        {
            using var response = await landscape.Browser.GetAsync("/jobs");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
