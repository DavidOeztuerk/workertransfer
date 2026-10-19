using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Noelia.Abstractions.Caching;
using Noelia.Infrastructure.Middleware;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using WorkerTransfer.Gateway;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// The real gateway with the real route map, in front of eleven fakes.
/// </summary>
/// <remarks>
/// On <strong>real</strong> ports, not over a test host: Ocelot calls its
/// destinations over HTTP, and a <c>TestServer</c> has no socket. A test that
/// went around this would check a different wiring from the one that runs.
/// <para>
/// The route map is read from the source tree and bent in one place only: the
/// ports point at the fakes. Paths, priorities and the header rule stay as they
/// are shipped — otherwise the test would check its own copy.
/// </para>
/// </remarks>
public class Landscape : IAsyncLifetime
{
    /// <summary>Whether the routes are loaded in reverse order.</summary>
    /// <remarks>
    /// A map whose answer depended on the order of its lines would be a trap for
    /// the next person who appends a route. Measured: with the shipped
    /// <c>Priority</c> values reversing changes nothing, without them eight
    /// routes fall over. <see cref="ReversedLandscape"/> pins that.
    /// </remarks>
    protected virtual bool Reversed => false;

    /// <summary>The names as they stand in the route map.</summary>
    private static readonly string[] Names =
    [
        "identity", "consent", "profile", "resume", "portfolio", "jobs",
        "applications", "companies", "transfer", "notification", "github",
        "scout", "advisor", "assessment", "web"
    ];

    private readonly List<WebApplication> _fakes = [];
    private WebApplication _gateway = null!;

    /// <summary>Where the gateway listens.</summary>
    public HttpClient Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var ports = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in Names)
        {
            var fake = Fake(name);
            await fake.StartAsync();
            _fakes.Add(fake);
            ports[name] = PortOf(fake);
        }

        _gateway = Gateway(ports, Reversed);
        await _gateway.StartAsync();

        Browser = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{PortOf(_gateway)}") };
    }

    public async Task DisposeAsync()
    {
        Browser.Dispose();
        await _gateway.StopAsync();
        await _gateway.DisposeAsync();

        foreach (var fake in _fakes)
        {
            await fake.StopAsync();
            await fake.DisposeAsync();
        }
    }

    /// <summary>Who answered, and on which path?</summary>
    public async Task<(string Service, string Path)> Ask(
        string path, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await Browser.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ("<no route>", path);
        }

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return (body.GetProperty("service").GetString()!, body.GetProperty("path").GetString()!);
    }

    /// <summary>A service that says nothing but that it was the one.</summary>
    private static WebApplication Fake(string name)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var service = builder.Build();

        service.Map("/{**rest}", (HttpContext context) =>
            Results.Ok(new { service = name, path = context.Request.Path.Value }));

        return service;
    }

    /// <summary>The real gateway, with its ports bent onto the fakes.</summary>
    private static WebApplication Gateway(
        IReadOnlyDictionary<string, int> ports, bool reversed)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        builder.Configuration.AddJsonFile(
            RouteMapFile(ports, reversed), optional: false, reloadOnChange: false);
        builder.Services.AddOcelot(builder.Configuration);

        // Word for word as in `Program.cs` — Noelia's counter AND its middleware.
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
        builder.Services.Configure<DistributedRateLimitingOptions>(
            builder.Configuration.GetSection(DistributedRateLimitingOptions.SectionName));

        var gateway = builder.Build();

        // The same order as in `Program.cs`. It is part of what is checked
        // here: Ocelot ends the chain.
        gateway.UseHealthProbes();
        gateway.UseMiddleware<CorrelationIdMiddleware>();
        gateway.UseMiddleware<DistributedRateLimitingMiddleware>();
        gateway.UseOcelot().GetAwaiter().GetResult();

        return gateway;
    }

    /// <summary>The shipped route map, only with different ports.</summary>
    private static string RouteMapFile(IReadOnlyDictionary<string, int> ports, bool reversed)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var raw = File.ReadAllText(source);

        // `"Host": "jobs-service", "Port": 8006` -> the fake's port. The name
        // stays `127.0.0.1`, because that is where the fake listens.
        var bent = Regex.Replace(
            raw,
            "\"Host\": \"(?<name>[a-z]+)(-service)?\",\\s*\n\\s*\"Port\": \\d+",
            hit => "\"Host\": \"127.0.0.1\",\n          \"Port\": "
                       + ports[hit.Groups["name"].Value]);

        if (reversed)
        {
            bent = ReverseRoutes(bent);
        }

        var target = Path.Combine(Path.GetTempPath(), $"ocelot-{Guid.NewGuid():N}.json");
        File.WriteAllText(target, bent);

        return target;
    }

    /// <summary>The same routes, in reverse order.</summary>
    private static string ReverseRoutes(string map)
    {
        var parsed = JsonDocument.Parse(
            map, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var routes = parsed.RootElement.GetProperty("Routes")
            .EnumerateArray().Reverse().ToList();

        var reversedMap = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Routes"] = routes,
            // The rate limiter travels along: the reversed landscape should
            // differ ONLY in the order of its lines, otherwise it checks
            // something else on the side.
            ["DistributedRateLimiting"] =
                parsed.RootElement.GetProperty("DistributedRateLimiting"),
            ["GlobalConfiguration"] = parsed.RootElement.GetProperty("GlobalConfiguration"),
        };

        return JsonSerializer.Serialize(reversedMap);
    }

    private static int PortOf(WebApplication application) =>
        new Uri(application.Urls.First()).Port;
}

[CollectionDefinition(Name)]
public sealed class LandscapeCollection : ICollectionFixture<Landscape>
{
    public const string Name = "landscape";
}

/// <summary>The same landscape, with the route map reversed.</summary>
public sealed class ReversedLandscape : Landscape
{
    /// <inheritdoc />
    protected override bool Reversed => true;
}

[CollectionDefinition(Name)]
public sealed class ReversedCollection : ICollectionFixture<ReversedLandscape>
{
    public const string Name = "landscape-reversed";
}


/// <summary>
/// The same landscape, but with counters of its own.
/// </summary>
/// <remarks>
/// The rate-limit tests deliberately exhaust limits. If they did that in the
/// shared landscape, the outcome of <c>RoutingTests</c> would depend on who
/// ran first — and a test whose outcome depends on the order is worse than
/// none.
/// </remarks>
public sealed class BrakedLandscape : Landscape;

[CollectionDefinition(Name)]
public sealed class BrakeCollection : ICollectionFixture<BrakedLandscape>
{
    public const string Name = "landscape-brake";
}
