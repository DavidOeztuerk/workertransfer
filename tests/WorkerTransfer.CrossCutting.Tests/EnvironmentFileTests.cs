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
                @"\$\{(?<name>WORKERTRANSFER_[A-Z_]+)[:}]",
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

    /// <summary>What reads as a secret by its name: a secret, a key, a token.</summary>
    private static readonly Regex SecretName = new(
        "(SECRET|SECRETS_KEY|PRIVATE_KEY|PUBLIC_KEY|TOKEN|API_KEY)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>Every secret the template names — derived, not listed.</summary>
    /// <remarks>
    /// A hand-written list stood here, with four names, while the template had
    /// grown to nine secrets: the master key and the evidence secret, both
    /// required, were never checked. A list of what to guard goes stale exactly
    /// when something new needs guarding.
    /// </remarks>
    private static string[] SecretNames() =>
        [.. FromTemplate().Where(name => SecretName.IsMatch(name))];

    /// <summary>The secrets <c>make env</c> rolls, and without which the stack does not start.</summary>
    private static readonly string[] RequiredSecretNames =
    [
        "WORKERTRANSFER_JWT_PRIVATE_KEY",
        "WORKERTRANSFER_JWT_PUBLIC_KEY",
        "WORKERTRANSFER_NOTIFY_SECRET",
        "WORKERTRANSFER_ERASURE_SECRET",
        "WORKERTRANSFER_SECRETS_KEY",
        "WORKERTRANSFER_EVIDENCE_SECRET"
    ];

    public static TheoryData<string> Secrets => new(SecretNames());

    public static TheoryData<string> RequiredSecrets => new(RequiredSecretNames);

    [Fact]
    public void The_secrets_are_found_at_all() =>
        SecretNames().Should().Contain(
            RequiredSecretNames,
            "otherwise the name pattern no longer finds the secrets and the two theories below check nothing");

    /// <summary>
    /// Every secret stands in the template <strong>empty</strong>.
    /// </summary>
    /// <remarks>
    /// The core of H2: a built-in default <em>is</em> the secret, and it
    /// therefore lies in git. <c>make env</c> rolls the required ones at
    /// creation time; the optional ones stay empty until somebody sets them.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Secrets))]
    public void No_secret_carries_a_value(string name)
    {
        var row = File.ReadAllLines(Path.Combine(Root(), ".env.example"))
            .Single(line => line.StartsWith($"{name}=", StringComparison.Ordinal));

        row.Should().Be(
            $"{name}=",
            "a default value would be the secret itself, and it would lie in git");
    }

    /// <summary>
    /// And in <c>docker-compose.yml</c> no secret has a default value.
    /// </summary>
    /// <remarks>
    /// The other half of the same promise. <c>${X:-default}</c> would be exactly
    /// the built-in secret. Allowed are <c>${X:?…}</c>, which aborts and names
    /// the name, and — for an optional secret such as the GitHub token —
    /// <c>${X:-}</c> with an EMPTY default: empty means "not set up".
    /// </remarks>
    [Theory]
    [MemberData(nameof(Secrets))]
    public void Compose_holds_no_default_for_any_secret(string name)
    {
        var withValue = new Regex(
            $@"\$\{{{Regex.Escape(name)}:-[^}}]",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        withValue.IsMatch(ComposeWithoutComments()).Should().BeFalse(
            $"'{name}' would then have a default value in git");
    }

    /// <summary>The required secrets abort the start and name themselves.</summary>
    [Theory]
    [MemberData(nameof(RequiredSecrets))]
    public void A_required_secret_aborts_the_start_when_missing(string name) =>
        ComposeWithoutComments().Should().Contain(
            $"${{{name}:?",
            $"'{name}' has to abort the start and name itself when it is missing");

    private static string ComposeWithoutComments()
    {
        var raw = File.ReadAllText(Path.Combine(Root(), "docker-compose.yml"));

        return string.Join(
            "\n", raw.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));
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

        entryPoints.Should().HaveCount(16, "fifteen services and the gateway");

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
