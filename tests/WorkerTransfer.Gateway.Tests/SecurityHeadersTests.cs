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
/// The rate limiter's rejection carries security headers — and otherwise the
/// gateway lays none over foreign responses.
/// </summary>
/// <remarks>
/// <para><strong>Both halves were paid for dearly.</strong> An audit report
/// stated that the gateway's own responses carried no security headers. That
/// was true for the rate limiter's 429 — it is an ordinary answer to an
/// ordinary caller and arrives at somebody who never reached a service.</para>
///
/// <para><strong>The first attempt to fix it broke the interface.</strong>
/// Noelia's <c>UseSecurityHeaders()</c> hung in front of the whole chain lays
/// its headers over everything <em>passed through</em> as well — and brings a
/// CSP with <c>script-src 'self'</c> and <c>upgrade-insecure-requests</c> with
/// it. Measured in the browser: a white page. Vite's module entry is inline and
/// its workers are <c>blob:</c>, both forbidden by the CSP; and
/// <c>upgrade-insecure-requests</c> rewrote <c>http://localhost:8090/…</c> to
/// <c>https://</c>, where nobody listens — in the browser log as a "TLS error"
/// for <c>main.tsx</c> and <c>config.js</c>.</para>
///
/// <para>That is why the headers now sit <strong>on the rejection
/// itself</strong> and not as a stage above it. The gateway answers almost
/// nothing itself; what it passes through belongs to the service behind it, and
/// that service's headers are its own business. A CSP is meaningless for a JSON
/// body anyway.</para>
///
/// <para>The second check below is the more important one: it pins that a
/// passed-through answer gets <em>no</em> CSP from the gateway. Without it the
/// same defect could come back at the next tidy-up, and from the outside it
/// would look like an improvement.</para>
/// </remarks>
public sealed class SecurityHeadersTests
{
    private const string OriginHeader = "X-Test-Origin";

    /// <summary>The host, in the same order as <c>Program.cs</c>.</summary>
    private static WebApplication BuildHost()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
        builder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            options.RequestsPerMinute = 0;
            options.RequestsPerHour = 0;
            options.RequestsPerDay = 0;
            options.Subject = RateLimitSubject.Origin;
            options.WhitelistedIps = [];
            options.WhitelistedEndpoints = [];
            options.EndpointSpecificLimits = new Dictionary<string, EndpointRateLimit>(
                StringComparer.Ordinal)
            {
                ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = 1 }
            };
        });

        var host = builder.Build();

        host.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(OriginHeader, out var value))
            {
                context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(value.ToString());
            }

            await next();
        });

        host.UseMiddleware<DistributedRateLimitingMiddleware>();

        // Stands for what Ocelot otherwise passes through: a foreign page that
        // brings its own headers with it (or precisely none).
        host.Run(async context =>
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<!doctype html><p>passed through");
        });

        return host;
    }

    private static HttpClient Browser(WebApplication host) =>
        new()
        {
            BaseAddress = new Uri(
                host.Urls.First(url => url.StartsWith("http://", StringComparison.Ordinal)))
        };

    /// <summary>Signing in twice at a limit of one — the second one is the 429.</summary>
    private static async Task<HttpResponseMessage> Rejected(WebApplication host, string origin)
    {
        using var browser = Browser(host);
        browser.DefaultRequestHeaders.Add(OriginHeader, origin);

        using var first = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        (await browser.PostAsync("/auth/login", first)).Dispose();

        using var second = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        return await browser.PostAsync("/auth/login", second);
    }

    /// <summary>The rejection carries the two headers that matter for it.</summary>
    [Fact]
    public async Task The_rejection_carries_the_headers()
    {
        await using var host = BuildHost();
        await host.StartAsync();

        using var response = await Rejected(host, "10.0.0.1");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().ContainSingle("DENY");
    }

    /// <summary>
    /// A passed-through answer gets NO CSP from the gateway — otherwise the
    /// interface is white.
    /// </summary>
    [Fact]
    public async Task What_is_passed_through_gets_no_CSP_from_the_gateway()
    {
        await using var host = BuildHost();
        await host.StartAsync();

        using var browser = Browser(host);
        using var response = await browser.GetAsync("/verify");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.Contains("Content-Security-Policy").Should().BeFalse(
            "a gateway CSP over foreign HTML forbids Vite's inline entry and "
            + "its blob: workers — the page stays white");

        response.Headers.TryGetValues("Content-Security-Policy", out var values);
        (values ?? []).Should().NotContain(
            value => value.Contains("upgrade-insecure-requests", StringComparison.Ordinal),
            "the browser would rewrite http://localhost:8090 to https://, where nobody listens");
    }
}
