using System.Text.RegularExpressions;
using FluentAssertions;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.CrossCutting.Tests;

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
public sealed class EnvironmentFileTests
{
    private static string Root() => RepositoryRoot.Find();

    /// <summary>The names docker-compose.yml expects from the environment.</summary>
    private static IReadOnlyList<string> FromCompose()
    {
        var raw = File.ReadAllText(Path.Combine(Root(), "docker-compose.yml"));

        // Comment lines out: the reason why an earlier default value is gone
        // stands there — together with its spelling.
        var withoutComments = string.Join(
            "\n",
            raw.Split('\n').Where(row => !row.TrimStart().StartsWith('#')));

        return [.. Regex.Matches(
                withoutComments,
                @"\$\{(?<name>WORKERTRANSFER_[A-Z_]+|ANTHROPIC_API_KEY)[:}]",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(hit => hit.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>The names the template sets.</summary>
    private static IReadOnlyList<string> FromTemplate() =>
        [.. File.ReadAllLines(Path.Combine(Root(), ".env.example"))
            .Where(row => !row.TrimStart().StartsWith('#'))
            .Select(row => row.Split('=', 2))
            .Where(parts => parts.Length == 2 && parts[0].Trim().Length > 0)
            .Select(parts => parts[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Every_key_from_compose_stands_in_the_template()
    {
        var needed = FromCompose();

        needed.Should().NotBeEmpty("otherwise this test does not find the keys at all");
        FromTemplate().Should().Contain(
            needed,
            ".env.example has to know every key docker-compose.yml expects");
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
        var row = File.ReadAllLines(Path.Combine(Root(), ".env.example"))
            .Single(line => line.StartsWith($"{name}=", StringComparison.Ordinal));

        row.Should().Be(
            $"{name}=",
            "a default value would be the secret itself, and it would lie in git");
    }

    /// <summary>
    /// And in <c>docker-compose.yml</c> none of them has a default.
    /// </summary>
    /// <remarks>
    /// The other half of the same promise. <c>${X:-default}</c> would be exactly
    /// the built-in secret; <c>${X:?…}</c> aborts and names the name.
    /// </remarks>
    [Theory]
    [InlineData("WORKERTRANSFER_JWT_PRIVATE_KEY")]
    [InlineData("WORKERTRANSFER_JWT_PUBLIC_KEY")]
    [InlineData("WORKERTRANSFER_NOTIFY_SECRET")]
    [InlineData("WORKERTRANSFER_ERASURE_SECRET")]
    public void Compose_holds_no_default_for_any_secret(string name)
    {
        var raw = File.ReadAllText(Path.Combine(Root(), "docker-compose.yml"));
        var withoutComments = string.Join(
            "\n", raw.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

        withoutComments.Should().NotContain(
            $"${{{name}:-",
            $"'{name}' would then have a default value in git");
        withoutComments.Should().Contain(
            $"${{{name}:?",
            $"'{name}' has to abort the start and name itself when it is missing");
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
        var entryPoints = Directory
            .EnumerateFiles(Path.Combine(Root(), "src"), "Program.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        entryPoints.Should().HaveCount(15, "fourteen services and the gateway");

        foreach (var path in entryPoints)
        {
            var text = File.ReadAllText(path);
            var load = text.IndexOf("EnvironmentFile.Load()", StringComparison.Ordinal);
            var build = text.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal);

            load.Should().BeGreaterThan(
                -1, $"'{Path.GetFileName(Path.GetDirectoryName(path))}' does not load the environment");
            load.Should().BeLessThan(
                build,
                $"'{Path.GetFileName(Path.GetDirectoryName(path))}' loads it too late — "
                + "the configuration builder has already read the environment by then");
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
        var folder = Directory.CreateTempSubdirectory("wt-env-");

        // Unique names, so that parallel suites do not get in each other's way.
        var id = Guid.NewGuid().ToString("N")[..8];
        var unsetName = $"WT_PROBE_NEW_{id}";
        var presetName = $"WT_PROBE_OLD_{id}";

        try
        {
            File.WriteAllText(
                Path.Combine(folder.FullName, ".env"),
                $"{unsetName}=from-the-file\n{presetName}=from-the-file\n");

            Environment.SetEnvironmentVariable(presetName, "from-the-environment");

            var loaded = EnvironmentFile.Load(folder.FullName);

            loaded.Should().EndWith(".env");
            Environment.GetEnvironmentVariable(unsetName).Should().Be("from-the-file");
            Environment.GetEnvironmentVariable(presetName).Should().Be(
                "from-the-environment",
                "a variable that is set wins — in the container it comes from outside");
        }
        finally
        {
            Environment.SetEnvironmentVariable(unsetName, null);
            Environment.SetEnvironmentVariable(presetName, null);
            folder.Delete(recursive: true);
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
        var folder = Directory.CreateTempSubdirectory("wt-env-empty-");

        try
        {
            EnvironmentFile.Load(folder.FullName).Should().BeNull();
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

}
