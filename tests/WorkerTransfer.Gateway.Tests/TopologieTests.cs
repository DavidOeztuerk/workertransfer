using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using YamlDotNet.Serialization;

namespace WorkerTransfer.Gateway.Tests;

/// <summary>
/// Drei Beschreibungen derselben Landschaft — und sie müssen dasselbe sagen.
/// </summary>
/// <remarks>
/// Die Landkarte des Gateways nennt Rechnernamen und Häfen, `docker-compose.yml`
/// legt sie an, und das Helm-Chart tut dasselbe noch einmal für ein Cluster.
/// Läuft eine der drei weg, merkt es niemand beim Bauen: Ocelot meldet einen
/// unerreichbaren Nachbarn erst beim ersten Aufruf, und ein Chart mit einem
/// fehlenden Dienst rollt sauber aus.
/// <para>
/// Zur Python-Zeit hielt <c>tests/test_k8s_matches_compose.py</c> das fest.
/// Diese Reihe ist ihr Nachfolger — sie stirbt nicht mit dem Python-Baum,
/// sondern zieht mit um.
/// </para>
/// </remarks>
public class TopologieTests
{
    /// <summary>Was die Landkarte des Gateways anspricht.</summary>
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

    /// <summary>Was Compose anlegt: Dienstname auf (SERVICE_DIR, Hafen).</summary>
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

    /// <summary>Was das Chart anlegt.</summary>
    private static readonly Lazy<IReadOnlyList<Dictionary<object, object>>> Chart = new(() =>
    {
        var wurzel = Repowurzel();
        var roh = File.ReadAllText(
            Path.Combine(wurzel, "deploy", "helm", "workertransfer", "values.yaml"));

        var gelesen = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(roh);

        return [.. ((List<object>)gelesen["services"]).Cast<Dictionary<object, object>>()];
    });

    /// <summary>Jeder Rechner, den die Landkarte nennt, wird auch angelegt.</summary>
    /// <remarks>
    /// Ein Tippfehler hier ist teuer: Ocelot antwortet dann mit 502, und zwar
    /// erst, wenn jemand die Route benutzt.
    /// </remarks>
    [Fact]
    public void Die_Landkarte_nennt_nur_Dienste_die_es_gibt()
    {
        foreach (var (rechner, hafen) in Landkarte.Value)
        {
            if (rechner == "web")
            {
                // Die Oberfläche ist kein .NET-Dienst und trägt kein
                // SERVICE_DIR — sie steht trotzdem in Compose.
                Compose.Value.Should().NotContainKey(rechner);
                continue;
            }

            Compose.Value.Should().ContainKey(rechner);
            Compose.Value[rechner].Hafen.Should().Be(
                hafen, $"die Landkarte spricht {rechner}:{hafen} an");
        }
    }

    /// <summary>Compose und das Chart legen dieselben Dienste an denselben Häfen an.</summary>
    [Fact]
    public void Compose_und_Chart_beschreiben_dieselbe_Landschaft()
    {
        var ausDemChart = Chart.Value.ToDictionary(
            entry => entry["name"].ToString()!,
            entry => (Dir: entry["dir"].ToString()!,
                        Hafen: int.Parse(entry["port"].ToString()!)),
            StringComparer.Ordinal);

        // `gateway` steht in Compose als Dienst, im Chart aber in einer eigenen
        // ResumeTemplate — es ist der Eingang, kein Nachbar.
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

    /// <summary>Jeder Dienst mit einer Datenbank hat eine, die angelegt wird.</summary>
    /// <remarks>
    /// ADR-0004 verbietet eine gemeinsame Datenbank. Fehlt eine im
    /// Anlegeskript, dreht sich der Dienst in „database … does not exist" im
    /// Kreis — und zwar nur auf einem frischen Datenträger, also nicht bei dem,
    /// der sie hinzugefügt hat.
    /// </remarks>
    [Fact]
    public void Jede_Dienstdatenbank_wird_auch_angelegt()
    {
        var wurzel = Repowurzel();
        var sql = File.ReadAllText(Path.Combine(
            wurzel, "scripts", "initdb", "01-create-service-databases.sql"));

        // Kommentare zuerst weg. Ohne das las eine auskommentierte Zeile wie eine
        // angelegte Datenbank — gemessen: die Gegenprobe „eine Datenbank fehlt"
        // blieb grün, weil `-- CREATE DATABASE notification` denselben Ausdruck
        // traf wie die echte Zeile.
        var ohneKommentar = Regex.Replace(
            sql, @"^\s*--.*$", string.Empty, RegexOptions.Multiline);

        var angelegt = Regex.Matches(ohneKommentar, @"CREATE DATABASE (\w+)")
            .Select(hit => hit.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // `identity` legt der Postgres-Einstiegspunkt selbst an (POSTGRES_DB).
        angelegt.Add("identity");

        foreach (var entry in Chart.Value)
        {
            var datenbank = entry["database"].ToString()!;

            angelegt.Should().Contain(
                datenbank, $"{entry["name"]} erwartet die Datenbank {datenbank}");
        }
    }

    /// <summary>Wo dieses Repository beginnt, durch Hochlaufen gefunden.</summary>
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
