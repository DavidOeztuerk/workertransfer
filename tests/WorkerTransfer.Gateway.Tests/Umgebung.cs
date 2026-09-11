using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Girder.Abstractions.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.RateLimiting;
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
/// Das echte Gateway mit der echten Landkarte, vor elf Attrappen.
/// </summary>
/// <remarks>
/// Auf <strong>echten</strong> Ports, nicht über einen Testwirt: Ocelot ruft
/// seine Ziele über HTTP, und ein <c>TestServer</c> hat keinen Socket. Ein
/// Test, der das umginge, prüfte eine andere Verdrahtung als die, die läuft.
/// <para>
/// Die Landkarte wird aus dem Quellbaum gelesen und nur an einer Stelle
/// verbogen: die Häfen zeigen auf die Attrappen. Pfade, Prioritäten und die
/// Kopfregel bleiben, wie sie ausgeliefert werden — sonst prüfte der Test seine
/// eigene Kopie.
/// </para>
/// </remarks>
public class Landschaft : IAsyncLifetime
{
    /// <summary>Ob die Routen in umgekehrter Reihenfolge geladen werden.</summary>
    /// <remarks>
    /// Eine Landkarte, deren Antwort von der Zeilenreihenfolge abhinge, wäre
    /// eine Falle für den Nächsten, der eine Route anhängt. Gemessen: mit den
    /// ausgelieferten <c>Priority</c>-Werten ändert das Umdrehen nichts, ohne
    /// sie fallen acht Routen um. <see cref="UmgedreheteLandschaft"/> hält das
    /// fest.
    /// </remarks>
    protected virtual bool Umgedreht => false;

    /// <summary>Die Namen, wie sie in der Landkarte stehen.</summary>
    private static readonly string[] Namen =
    [
        "identity", "consent", "profile", "resume", "portfolio", "jobs",
        "applications", "companies", "transfer", "notification", "github",
        "scout", "web"
    ];

    private readonly List<WebApplication> _attrappen = [];
    private WebApplication _gateway = null!;

    /// <summary>Wo das Gateway hört.</summary>
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

    /// <summary>Wer hat geantwortet, und auf welchen Pfad?</summary>
    public async Task<(string Dienst, string Pfad)> Frage(
        string pfad, params (string Name, string Wert)[] koepfe)
    {
        using var anfrage = new HttpRequestMessage(HttpMethod.Get, pfad);

        foreach (var (name, wert) in koepfe)
        {
            anfrage.Headers.TryAddWithoutValidation(name, wert);
        }

        using var antwort = await Browser.SendAsync(anfrage);

        if (antwort.StatusCode == HttpStatusCode.NotFound)
        {
            return ("<keine Route>", pfad);
        }

        var rumpf = JsonDocument.Parse(await antwort.Content.ReadAsStringAsync()).RootElement;

        return (rumpf.GetProperty("dienst").GetString()!, rumpf.GetProperty("pfad").GetString()!);
    }

    /// <summary>Ein Dienst, der nur sagt, dass er es war.</summary>
    private static WebApplication Attrappe(string name)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();

        var dienst = bau.Build();

        dienst.Map("/{**alles}", (HttpContext kontext) =>
            Results.Ok(new { dienst = name, pfad = kontext.Request.Path.Value }));

        return dienst;
    }

    /// <summary>Das echte Gateway, mit auf die Attrappen gebogenen Häfen.</summary>
    private static WebApplication Gateway(
        IReadOnlyDictionary<string, int> haefen, bool umgedreht)
    {
        var bau = WebApplication.CreateBuilder();
        bau.WebHost.UseUrls("http://127.0.0.1:0");
        bau.Logging.ClearProviders();

        bau.Configuration.AddJsonFile(
            Landkarte(haefen, umgedreht), optional: false, reloadOnChange: false);
        bau.Services.AddOcelot(bau.Configuration);

        // Wortgleich zu `Program.cs` — Girders Zaehler UND seine Zwischenschicht.
        bau.Services.AddMemoryCache();
        bau.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
        bau.Services.Configure<DistributedRateLimitingOptions>(
            bau.Configuration.GetSection(DistributedRateLimitingOptions.SectionName));

        var gateway = bau.Build();

        // Dieselbe Reihenfolge wie in `Program.cs`. Sie ist Teil dessen, was
        // hier geprueft wird: Ocelot beendet die Kette.
        gateway.UseGesundheit();
        gateway.UseMiddleware<CorrelationIdMiddleware>();
        gateway.UseMiddleware<DistributedRateLimitingMiddleware>();
        gateway.UseOcelot().GetAwaiter().GetResult();

        return gateway;
    }

    /// <summary>Die ausgelieferte Landkarte, nur mit anderen Häfen.</summary>
    private static string Landkarte(IReadOnlyDictionary<string, int> haefen, bool umgedreht)
    {
        var quelle = Path.Combine(AppContext.BaseDirectory, "ocelot.json");
        var roh = File.ReadAllText(quelle);

        // `"Host": "jobs-service", "Port": 8006` -> der Hafen der Attrappe.
        // Der Name bleibt `127.0.0.1`, weil die Attrappe dort hoert.
        var gebogen = Regex.Replace(
            roh,
            "\"Host\": \"(?<name>[a-z]+)(-service)?\",\\s*\n\\s*\"Port\": \\d+",
            treffer => "\"Host\": \"127.0.0.1\",\n          \"Port\": "
                       + haefen[treffer.Groups["name"].Value]);

        if (umgedreht)
        {
            gebogen = RoutenUmdrehen(gebogen);
        }

        var ziel = Path.Combine(Path.GetTempPath(), $"ocelot-{Guid.NewGuid():N}.json");
        File.WriteAllText(ziel, gebogen);

        return ziel;
    }

    /// <summary>Dieselben Routen, in umgekehrter Reihenfolge.</summary>
    private static string RoutenUmdrehen(string karte)
    {
        var gelesen = JsonDocument.Parse(
            karte, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var routen = gelesen.RootElement.GetProperty("Routes")
            .EnumerateArray().Reverse().ToList();

        var neu = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Routes"] = routen,
            // Die Bremse reist mit: die umgedrehte Landschaft soll sich NUR in
            // der Zeilenreihenfolge unterscheiden, sonst prueft sie nebenbei
            // etwas anderes.
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

/// <summary>Dieselbe Landschaft, mit umgedrehter Landkarte.</summary>
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
/// Dieselbe Landschaft, aber mit eigenen Zählern.
/// </summary>
/// <remarks>
/// Die Bremstests schöpfen Grenzen absichtlich aus. Täten sie das in der
/// gemeinsamen Landschaft, hinge das Ergebnis von <c>LandkarteTests</c> davon
/// ab, wer zuerst lief — und ein Test, dessen Ausgang von der Reihenfolge
/// abhängt, ist schlimmer als keiner.
/// </remarks>
public sealed class GebremsteLandschaft : Landschaft;

[CollectionDefinition(Name)]
public sealed class BremsSammlung : ICollectionFixture<GebremsteLandschaft>
{
    public const string Name = "landschaft-bremse";
}
