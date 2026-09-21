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
public class Landschaft : IAsyncLifetime
{
    /// <summary>Whether the routes are loaded in reverse order.</summary>
    /// <remarks>
    /// A map whose answer depended on the order of its lines would be a trap for
    /// the next person who appends a route. Measured: with the shipped
    /// <c>Priority</c> values reversing changes nothing, without them eight
    /// routes fall over. <see cref="UmgedreheteLandschaft"/> pins that.
    /// </remarks>
    protected virtual bool Umgedreht => false;

    /// <summary>The names as they stand in the route map.</summary>
    private static readonly string[] Namen =
    [
        "identity", "consent", "profile", "resume", "portfolio", "jobs",
        "applications", "companies", "transfer", "notification", "github",
        "scout", "advisor", "assessment", "web"
    ];

    private readonly List<WebApplication> _attrappen = [];
    private WebApplication _gateway = null!;

    /// <summary>Where the gateway listens.</summary>
    public HttpClient Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var haefen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var name in Namen)
        {
            var attrappe = Attrappe(name);
            await attrappe.StartAsync();
            _attrappen.Add(attrappe);
            haefen[name] = Hafen(attrappe);
        }

        _gateway = Gateway(haefen, Umgedreht);
        await _gateway.StartAsync();

        Browser = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Hafen(_gateway)}") };
    }

    public async Task DisposeAsync()
    {
        Browser.Dispose();
        await _gateway.StopAsync();
        await _gateway.DisposeAsync();

        foreach (var attrappe in _attrappen)
        {
            await attrappe.StopAsync();
            await attrappe.DisposeAsync();
        }
    }

    /// <summary>Who answered, and on which path?</summary>
    public async Task<(string Dienst, string Pfad)> Frage(
        string path, params (string Name, string Wert)[] headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await Browser.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ("<keine Route>", path);
        }

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        return (body.GetProperty("service").GetString()!, body.GetProperty("path").GetString()!);
    }

    /// <summary>A service that says nothing but that it was the one.</summary>
    private static WebApplication Attrappe(string name)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();

        var service = bau.Build();

        service.Map("/{**alles}", (HttpContext context) =>
            Results.Ok(new { service = name, path = context.Request.Path.Value }));

        return service;
    }

    /// <summary>The real gateway, with its ports bent onto the fakes.</summary>
    private static WebApplication Gateway(
        IReadOnlyDictionary<string, int> haefen, bool umgedreht)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();

        bau.Configuration.AddJsonFile(
            Landkarte(haefen, umgedreht), optional: false, reloadOnChange: false);
        bau.Services.AddOcelot(bau.Configuration);

        // Word for word as in `Program.cs` — Noelia's counter AND its middleware.
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
        bau.Services.Configure<DistributedRateLimitingOptions>(
            bau.Configuration.GetSection(DistributedRateLimitingOptions.SectionName));

        var gateway = bau.Build();

        // The same order as in `Program.cs`. It is part of what is checked
        // here: Ocelot ends the chain.
        gateway.UseGesundheit();
        gateway.UseMiddleware<CorrelationIdMiddleware>();
        gateway.UseMiddleware<DistributedRateLimitingMiddleware>();
        gateway.UseOcelot().GetAwaiter().GetResult();

        return gateway;
    }

    /// <summary>The shipped route map, only with different ports.</summary>
    private static string Landkarte(IReadOnlyDictionary<string, int> haefen, bool umgedreht)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var roh = File.ReadAllText(source);

        // `"Host": "jobs-service", "Port": 8006` -> the fake's port. The name
        // stays `127.0.0.1`, because that is where the fake listens.
        var gebogen = Regex.Replace(
            roh,
            "\"Host\": \"(?<name>[a-z]+)(-service)?\",\\s*\n\\s*\"Port\": \\d+",
            hit => "\"Host\": \"127.0.0.1\",\n          \"Port\": "
                       + haefen[hit.Groups["name"].Value]);

        if (umgedreht)
        {
            gebogen = RoutenUmdrehen(gebogen);
        }

        var target = Path.Combine(Path.GetTempPath(), $"ocelot-{Guid.NewGuid():N}.json");
        File.WriteAllText(target, gebogen);

        return target;
    }

    /// <summary>The same routes, in reverse order.</summary>
    private static string RoutenUmdrehen(string karte)
    {
        var gelesen = JsonDocument.Parse(
            karte, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var routen = gelesen.RootElement.GetProperty("Routes")
            .EnumerateArray().Reverse().ToList();

        var neu = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Routes"] = routen,
            // The rate limiter travels along: the reversed landscape should
            // differ ONLY in the order of its lines, otherwise it checks
            // something else on the side.
            ["DistributedRateLimiting"] =
                gelesen.RootElement.GetProperty("DistributedRateLimiting"),
            ["GlobalConfiguration"] = gelesen.RootElement.GetProperty("GlobalConfiguration"),
        };

        return JsonSerializer.Serialize(neu);
    }

    private static int Hafen(WebApplication anwendung) =>
        new Uri(anwendung.Urls.First()).Port;
}

[CollectionDefinition(Name)]
public sealed class LandschaftsSammlung : ICollectionFixture<Landschaft>
{
    public const string Name = "landschaft";
}

/// <summary>The same landscape, with the route map reversed.</summary>
public sealed class UmgedreheteLandschaft : Landschaft
{
    /// <inheritdoc />
    protected override bool Umgedreht => true;
}

[CollectionDefinition(Name)]
public sealed class UmgedrehteSammlung : ICollectionFixture<UmgedreheteLandschaft>
{
    public const string Name = "landschaft-umgedreht";
}


/// <summary>
/// The same landscape, but with counters of its own.
/// </summary>
/// <remarks>
/// The rate-limit tests deliberately exhaust limits. If they did that in the
/// shared landscape, the outcome of <c>LandkarteTests</c> would depend on who
/// ran first — and a test whose outcome depends on the order is worse than
/// none.
/// </remarks>
public sealed class GebremsteLandschaft : Landschaft;

[CollectionDefinition(Name)]
public sealed class BremsSammlung : ICollectionFixture<GebremsteLandschaft>
{
    public const string Name = "landschaft-bremse";
}
