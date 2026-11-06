using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using YamlDotNet.RepresentationModel;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>Each service carries its own secrets and no other's (ADR-0058).</summary>
/// <remarks>
/// One value in the anchor is one value in fifteen containers, and whoever takes
/// over one service holds it for all of them. That is the state these checks keep
/// from coming back. They read what a container really gets: its own block with
/// the anchor resolved into it.
/// </remarks>
public sealed class ServiceSecretTests
{
    private static string Root => RepositoryRoot.Find();

    public static TheoryData<string> Databases => new(RepositoryRoot.Services());

    [Fact]
    public void Compose_is_read_at_all()
    {
        Environments().Keys.Should().Contain(["postgres", "identity-service", "messages-service"]);

        Environments()["identity-service"].Keys.Should().Contain(
            "Erasure__Secret", "otherwise the anchor was not resolved");
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public void A_service_logs_in_with_its_own_role_and_password(string database)
    {
        var upper = database.ToUpperInvariant();
        var environment = Environments()[$"{database}-service"];

        environment[$"ConnectionStrings__{database}"].Should()
            .Contain($"Username=svc_{database};")
            .And.Contain($"Password=${{WORKERTRANSFER_DB_PASSWORD_{upper}:?");

        environment["WORKERTRANSFER_KEYRING_KEY"].Should()
            .StartWith($"${{WORKERTRANSFER_KEYRING_KEY_{upper}:?");

        environment["Evidence__Secret"].Should()
            .StartWith($"${{WORKERTRANSFER_EVIDENCE_SECRET_{upper}:?");
    }

    /// <summary>A second carrier would be the old state again, under a new name.</summary>
    [Theory]
    [MemberData(nameof(Databases))]
    public void A_service_carries_no_secret_of_another(string database)
    {
        var others = RepositoryRoot.Services()
            .Where(other => other != database)
            .Select(other => other.ToUpperInvariant())
            .ToList();

        var foreign = Environments()[$"{database}-service"].Values
            .Where(value => others.Any(other => Regex.IsMatch(
                value,
                $@"WORKERTRANSFER_(DB_PASSWORD|KEYRING_KEY|EVIDENCE_SECRET)_{other}\b",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))));

        foreign.Should().BeEmpty();
    }

    [Fact]
    public void Only_identity_holds_the_master_key() =>
        Carriers("WORKERTRANSFER_SECRETS_KEY").Should().Equal(
            ["identity-service"],
            "with it, and the row out of identity's database, every AI key opens");

    [Fact]
    public void Only_postgres_gets_the_bootstrap_password() =>
        Carriers("WORKERTRANSFER_POSTGRES_PASSWORD").Should().Equal(["postgres"]);

    /// <summary>The script has to set them, and that is all this widens.</summary>
    [Theory]
    [MemberData(nameof(Databases))]
    public void Postgres_gets_every_password_under_the_name_the_script_reads(string database)
    {
        var variable = $"WORKERTRANSFER_DB_PASSWORD_{database.ToUpperInvariant()}";

        Environments()["postgres"][variable].Should().StartWith($"${{{variable}:?");
    }

    [Fact]
    public void The_healthcheck_asks_a_database_no_service_owns()
    {
        var compose = File.ReadAllText(Path.Combine(Root, "docker-compose.yml"));

        compose.Should().Contain("psql -h 127.0.0.1 -U worker -d postgres");
    }

    /// <summary>A built-in default IS the secret, and this one opened everything.</summary>
    [Fact]
    public void No_connection_string_logs_in_as_the_superuser()
    {
        var places = Directory.EnumerateFiles(Path.Combine(Root, "src"), "appsettings*.json", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "deploy"), "*", SearchOption.AllDirectories))
            .Append(Path.Combine(Root, "docker-compose.yml"))
            .ToList();

        places.Should().Contain(file => file.EndsWith("docker-compose.yml", StringComparison.Ordinal));

        places
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"Username=worker|Password=worker|postgresPassword:\s*worker",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)))
            .Should().BeEmpty();
    }

    /// <summary>The second list of names, beside the one in the script.</summary>
    [Fact]
    public void The_secret_script_names_exactly_the_services_in_the_tree()
    {
        var list = File.ReadAllText(Path.Combine(Root, "scripts", "service-secrets.sh"));

        Regex.Match(list, "^SERVICE_DATABASES=\"(?<names>[a-z ]+)\"", RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .Groups["names"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Should().BeEquivalentTo(RepositoryRoot.Services());
    }

    /// <summary>The one step an owner runs against a file only they hold.</summary>
    [Fact]
    public async Task Filling_a_env_file_adds_whole_values_and_touches_nothing_else()
    {
        var file = Path.GetTempFileName();

        try
        {
            // No last newline: a key added after it would land on this value.
            await File.WriteAllTextAsync(
                file, "THIRD_PARTY_SECRET=keep-me\nWORKERTRANSFER_DB_PASSWORD_CONSENT=already-set");

            var (exitCode, output) = await Fill(file);

            exitCode.Should().Be(0, output);

            var lines = await File.ReadAllLinesAsync(file);

            lines[0].Should().Be("THIRD_PARTY_SECRET=keep-me");
            lines[1].Should().Be("WORKERTRANSFER_DB_PASSWORD_CONSENT=already-set");

            var added = lines.Skip(2).Select(line => line.Split('=', 2)).ToList();

            added.Select(pair => pair[0]).Should().Contain(
            [
                "WORKERTRANSFER_POSTGRES_PASSWORD",
                "WORKERTRANSFER_DB_PASSWORD_IDENTITY",
                "WORKERTRANSFER_KEYRING_KEY_CONSENT",
                "WORKERTRANSFER_EVIDENCE_SECRET_MESSAGES",
            ]);

            // Plain hex: `=`, `+` and `/` would otherwise stand in a connection string.
            added.Should().OnlyContain(pair => Regex.IsMatch(
                pair[1], "^[0-9a-f]{64}$", RegexOptions.None, TimeSpan.FromSeconds(5)));

            added.Select(pair => pair[1]).Should().OnlyHaveUniqueItems("one value per service is the point");

            foreach (var pair in added)
            {
                output.Should().NotContain(pair[1], "no value is printed");
            }

            (await Fill(file)).ExitCode.Should().Be(0);
            (await File.ReadAllLinesAsync(file)).Should().Equal(lines, "a second run finds everything set");
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static async Task<(int ExitCode, string Output)> Fill(string file)
    {
        var start = new ProcessStartInfo("bash")
        {
            ArgumentList = { "scripts/env-services.sh", file },
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var shell = Process.Start(start)!;
        var standardOutput = shell.StandardOutput.ReadToEndAsync();
        var standardError = shell.StandardError.ReadToEndAsync();
        await shell.WaitForExitAsync();

        return (shell.ExitCode, await standardOutput + await standardError);
    }

    /// <summary>Which containers name a variable, as a key or inside a value.</summary>
    private static List<string> Carriers(string variable) =>
        [.. Environments()
            .Where(service => service.Value.Any(entry =>
                entry.Key == variable || entry.Value.Contains($"${{{variable}:", StringComparison.Ordinal)))
            .Select(service => service.Key)];

    /// <summary>What each container gets, with the anchor resolved into it.</summary>
    private static Dictionary<string, Dictionary<string, string>> Environments()
    {
        var stream = new YamlStream();

        using (var reader = new StreamReader(Path.Combine(Root, "docker-compose.yml")))
        {
            stream.Load(reader);
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];
        var environments = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var (name, body) in services.Children)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            if (((YamlMappingNode)body).Children.TryGetValue(new YamlScalarNode("environment"), out var environment))
            {
                Collect((YamlMappingNode)environment, values);
            }

            environments[((YamlScalarNode)name).Value!] = values;
        }

        return environments;
    }

    private static void Collect(YamlMappingNode node, Dictionary<string, string> values)
    {
        foreach (var (key, value) in node.Children)
        {
            var name = ((YamlScalarNode)key).Value!;

            if (name == "<<")
            {
                Collect((YamlMappingNode)value, values);
            }
            else
            {
                values[name] = ((YamlScalarNode)value).Value ?? string.Empty;
            }
        }
    }
}
