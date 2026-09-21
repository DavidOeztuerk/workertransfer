using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using YamlDotNet.Serialization;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Three descriptions of the same landscape — and they have to say the same
/// thing.
/// </summary>
/// <remarks>
/// The gateway's route map names host names and ports, `docker-compose.yml`
/// creates them, and the Helm chart does the same thing once more for a
/// cluster. If one of the three runs away, nobody notices at build time: Ocelot
/// reports an unreachable neighbour only at the first call, and a chart with a
/// missing service rolls out cleanly.
/// <para>
/// In the Python era <c>tests/test_k8s_matches_compose.py</c> pinned this. This
/// suite is its successor — it does not die with the Python tree, it moves
/// along with it.
/// </para>
/// </remarks>
public class TopologieTests
{
    /// <summary>What the gateway's route map addresses.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Landkarte = new(() =>
    {
        var roh = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ocelot.json"));

        using var gelesen = JsonDocument.Parse(
            roh, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var targets = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var route in gelesen.RootElement.GetProperty("Routes").EnumerateArray())
        {
            foreach (var target in route.GetProperty("DownstreamHostAndPorts").EnumerateArray())
            {
                targets[target.GetProperty("Host").GetString()!] = target.GetProperty("Port").GetInt32();
            }
        }

        return targets;
    });

    /// <summary>What compose creates: service name to (SERVICE_DIR, port).</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, (string Dir, int Hafen)>> Compose =
        new(() =>
        {
            var wurzel = Repowurzel();
            var roh = File.ReadAllText(Path.Combine(wurzel, "docker-compose.yml"));
            var gelesen = new DeserializerBuilder().Build()
                .Deserialize<Dictionary<string, object>>(roh);

            var services = (Dictionary<object, object>)gelesen["services"];
            var found = new Dictionary<string, (string, int)>(StringComparer.Ordinal);

            foreach (var (name, block) in services)
            {
                if (block is not Dictionary<object, object> fields
                    || !fields.TryGetValue("environment", out var umgebung)
                    || umgebung is not Dictionary<object, object> values
                    || !values.TryGetValue("SERVICE_DIR", out var dir))
                {
                    continue;
                }

                var urls = values["ASPNETCORE_URLS"]!.ToString()!;
                var hafen = int.Parse(Regex.Match(urls, @":(\d+)").Groups[1].Value);

                found[name.ToString()!] = (dir.ToString()!, hafen);
            }

            return found;
        });

    /// <summary>What the chart creates.</summary>
    private static readonly Lazy<IReadOnlyList<Dictionary<object, object>>> Chart = new(() =>
    {
        var wurzel = Repowurzel();
        var roh = File.ReadAllText(
            Path.Combine(wurzel, "deploy", "helm", "workertransfer", "values.yaml"));

        var gelesen = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(roh);

        return [.. ((List<object>)gelesen["services"]).Cast<Dictionary<object, object>>()];
    });

    /// <summary>Every host the route map names is also created.</summary>
    /// <remarks>
    /// A typo here is expensive: Ocelot then answers with a 502, and only once
    /// somebody uses the route.
    /// </remarks>
    [Fact]
    public void The_route_map_names_only_services_that_exist()
    {
        foreach (var (rechner, hafen) in Landkarte.Value)
        {
            if (rechner == "web")
            {
                // The interface is not a .NET service and carries no
                // SERVICE_DIR — it stands in compose all the same.
                Compose.Value.Should().NotContainKey(rechner);
                continue;
            }

            Compose.Value.Should().ContainKey(rechner);
            Compose.Value[rechner].Hafen.Should().Be(
                hafen, $"die Landkarte spricht {rechner}:{hafen} an");
        }
    }

    /// <summary>Compose and the chart create the same services on the same ports.</summary>
    [Fact]
    public void Compose_and_the_chart_describe_the_same_landscape()
    {
        var ausDemChart = Chart.Value.ToDictionary(
            entry => entry["name"].ToString()!,
            entry => (Dir: entry["dir"].ToString()!,
                        Hafen: int.Parse(entry["port"].ToString()!)),
            StringComparer.Ordinal);

        // `gateway` stands in compose as a service, but in the chart in a
        // template of its own — it is the entrance, not a neighbour.
        var ausCompose = Compose.Value
            .Where(entry => entry.Key != "gateway")
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        ausDemChart.Keys.Should().BeEquivalentTo(ausCompose.Keys);

        foreach (var (name, compose) in ausCompose)
        {
            ausDemChart[name].Dir.Should().Be(compose.Dir, $"{name}: SERVICE_DIR");
            ausDemChart[name].Hafen.Should().Be(compose.Hafen, $"{name}: Hafen");
        }
    }

    /// <summary>Every service with a database has one that is created.</summary>
    /// <remarks>
    /// ADR-0004 forbids a shared database. If one is missing from the setup
    /// script, the service spins in "database … does not exist" — and only on a
    /// fresh volume, so not for the person who added it.
    /// </remarks>
    [Fact]
    public void Every_service_database_is_created_too()
    {
        var wurzel = Repowurzel();
        var sql = File.ReadAllText(Path.Combine(
            wurzel, "scripts", "initdb", "01-create-service-databases.sql"));

        // Comments away first. Without that a commented-out line read like a
        // created database — measured: the counter-probe "a database is
        // missing" stayed green, because `-- CREATE DATABASE notification` hit
        // the same expression as the real line.
        var ohneKommentar = Regex.Replace(
            sql, @"^\s*--.*$", string.Empty, RegexOptions.Multiline);

        var angelegt = Regex.Matches(ohneKommentar, @"CREATE DATABASE (\w+)")
            .Select(hit => hit.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // `identity` is created by the Postgres entrypoint itself (POSTGRES_DB).
        angelegt.Add("identity");

        foreach (var entry in Chart.Value)
        {
            var datenbank = entry["database"].ToString()!;

            angelegt.Should().Contain(
                datenbank, $"{entry["name"]} erwartet die Datenbank {datenbank}");
        }
    }

    /// <summary>Where this repository begins, found by walking upwards.</summary>
    private static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "CLAUDE.md")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
            ?? throw new InvalidOperationException("Die Repowurzel wurde nicht gefunden.");
    }
}
