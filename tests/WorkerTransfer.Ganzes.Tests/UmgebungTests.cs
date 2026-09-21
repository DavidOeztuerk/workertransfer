using System.Text.RegularExpressions;
using FluentAssertions;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// <c>.env.example</c> is complete — and carries no secret.
/// </summary>
/// <remarks>
/// <para>A template that does not know a key is worse than none: it looks
/// complete. Whoever copies it gets a stack that aborts at startup — and looks
/// for the mistake at their own end.</para>
///
/// <para>The check runs against <c>docker-compose.yml</c>, because what is
/// really needed stands there. Both directions: no <c>${…}</c> without an entry
/// in the template, and no entry in the template that nobody reads.</para>
/// </remarks>
public sealed class UmgebungTests
{
    private static string Wurzel() => Postgres_Ersatz2.Repowurzel();

    /// <summary>The names docker-compose.yml expects from the environment.</summary>
    private static IReadOnlyList<string> AusCompose()
    {
        var roh = File.ReadAllText(Path.Combine(Wurzel(), "docker-compose.yml"));

        // Comment lines out: the reason why an earlier default value is gone
        // stands there — together with its spelling.
        var ohneKommentar = string.Join(
            "\n",
            roh.Split('\n').Where(row => !row.TrimStart().StartsWith('#')));

        return [.. Regex.Matches(
                ohneKommentar,
                @"\$\{(?<name>WORKERTRANSFER_[A-Z_]+|ANTHROPIC_API_KEY)[:}]",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(hit => hit.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The names the template sets.</summary>
    private static IReadOnlyList<string> AusVorlage() =>
        [.. File.ReadAllLines(Path.Combine(Wurzel(), ".env.example"))
            .Where(row => !row.TrimStart().StartsWith('#'))
            .Select(row => row.Split('=', 2))
            .Where(teile => teile.Length == 2 && teile[0].Trim().Length > 0)
            .Select(teile => teile[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Every_key_from_compose_stands_in_the_template()
    {
        var gebraucht = AusCompose();

        gebraucht.Should().NotBeEmpty("sonst findet dieser Test die Schluessel gar nicht");
        AusVorlage().Should().Contain(
            gebraucht,
            ".env.example muss jeden Schluessel kennen, den docker-compose.yml erwartet");
    }

    /// <summary>
    /// The three secrets stand in the template <strong>empty</strong>.
    /// </summary>
    /// <remarks>
    /// The core of H2: a built-in default <em>is</em> the secret, and it
    /// therefore lies in git. <c>make env</c> rolls them at creation time.
    /// </remarks>
    [Theory]
    [InlineData("WORKERTRANSFER_JWT_PRIVATE_KEY")]
    [InlineData("WORKERTRANSFER_JWT_PUBLIC_KEY")]
    [InlineData("WORKERTRANSFER_NOTIFY_SECRET")]
    [InlineData("WORKERTRANSFER_ERASURE_SECRET")]
    public void No_secret_carries_a_value(string name)
    {
        var row = File.ReadAllLines(Path.Combine(Wurzel(), ".env.example"))
            .Single(z => z.StartsWith($"{name}=", StringComparison.Ordinal));

        row.Should().Be(
            $"{name}=",
            "ein Vorgabewert waere das Geheimnis selbst, und es laege in git");
    }

    /// <summary>
    /// And in <c>docker-compose.yml</c> none of them has a default.
    /// </summary>
    /// <remarks>
    /// The other half of the same promise. <c>${X:-vorgabe}</c> would be exactly
    /// the built-in secret; <c>${X:?…}</c> aborts and names the name.
    /// </remarks>
    [Theory]
    [InlineData("WORKERTRANSFER_JWT_PRIVATE_KEY")]
    [InlineData("WORKERTRANSFER_JWT_PUBLIC_KEY")]
    [InlineData("WORKERTRANSFER_NOTIFY_SECRET")]
    [InlineData("WORKERTRANSFER_ERASURE_SECRET")]
    public void Compose_holds_no_default_for_any_secret(string name)
    {
        var roh = File.ReadAllText(Path.Combine(Wurzel(), "docker-compose.yml"));
        var ohneKommentar = string.Join(
            "\n", roh.Split('\n').Where(z => !z.TrimStart().StartsWith('#')));

        ohneKommentar.Should().NotContain(
            $"${{{name}:-",
            $"'{name}' haette dann einen Vorgabewert in git");
        ohneKommentar.Should().Contain(
            $"${{{name}:?",
            $"'{name}' muss den Start abbrechen und sich benennen, wenn es fehlt");
    }

    /// <summary>Every entry point loads the environment file, and first of all.</summary>
    /// <remarks>
    /// "First of all" is not cosmetics: the configuration builder reads the
    /// environment variables while building the <c>WebApplicationBuilder</c>.
    /// Whoever loads afterwards has loaded and nobody reads it.
    /// </remarks>
    [Fact]
    public void Every_service_loads_the_environment_before_building()
    {
        var einstiege = Directory
            .EnumerateFiles(Path.Combine(Wurzel(), "src"), "Program.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        einstiege.Should().HaveCount(15, "vierzehn Dienste und das Gateway");

        foreach (var path in einstiege)
        {
            var text = File.ReadAllText(path);
            var laden = text.IndexOf("EnvironmentFile.Load()", StringComparison.Ordinal);
            var bauen = text.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal);

            laden.Should().BeGreaterThan(
                -1, $"'{Path.GetFileName(Path.GetDirectoryName(path))}' laedt die Umgebung nicht");
            laden.Should().BeLessThan(
                bauen,
                $"'{Path.GetFileName(Path.GetDirectoryName(path))}' laedt sie zu spaet — "
                + "der Konfigurationsaufbau hat die Umgebung dann schon gelesen");
        }
    }

    /// <summary>
    /// <c>EnvironmentFile.Load()</c> sets what is missing — and does not
    /// overwrite what is there.
    /// </summary>
    /// <remarks>
    /// The second half is the more important one. In compose and in Kubernetes
    /// the environment comes from there; a file left lying in the image must
    /// never override that — otherwise the content of the image would decide
    /// about the installation's secrets.
    /// </remarks>
    [Fact]
    public void Loading_sets_what_is_missing_and_leaves_what_is_set()
    {
        var ordner = Directory.CreateTempSubdirectory("wt-env-");

        // Unique names, so that parallel suites do not get in each other's way.
        var id = Guid.NewGuid().ToString("N")[..8];
        var neuer = $"WT_PROBE_NEU_{id}";
        var alter = $"WT_PROBE_ALT_{id}";

        try
        {
            File.WriteAllText(
                Path.Combine(ordner.FullName, ".env"),
                $"{neuer}=aus-der-datei\n{alter}=aus-der-datei\n");

            Environment.SetEnvironmentVariable(alter, "aus-der-umgebung");

            var geladen = EnvironmentFile.Load(ordner.FullName);

            geladen.Should().EndWith(".env");
            Environment.GetEnvironmentVariable(neuer).Should().Be("aus-der-datei");
            Environment.GetEnvironmentVariable(alter).Should().Be(
                "aus-der-umgebung",
                "eine gesetzte Variable gewinnt — im Container kommt sie von aussen");
        }
        finally
        {
            Environment.SetEnvironmentVariable(neuer, null);
            Environment.SetEnvironmentVariable(alter, null);
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>Without a file nothing happens, and that is not an error.</summary>
    /// <remarks>
    /// In a container there is no <c>.env</c> — the environment comes from
    /// compose or from the chart. An abort here would be an abort in the normal
    /// case.
    /// </remarks>
    [Fact]
    public void Without_a_file_nothing_happens()
    {
        var ordner = Directory.CreateTempSubdirectory("wt-env-leer-");

        try
        {
            EnvironmentFile.Load(ordner.FullName).Should().BeNull();
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

}
