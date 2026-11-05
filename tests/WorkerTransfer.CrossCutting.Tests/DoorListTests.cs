using System.Diagnostics;
using System.Text.RegularExpressions;
using FluentAssertions;
using Noelia.Infrastructure.Security.ServiceTokens;
using WorkerTransfer.ServiceDefaults;
using YamlDotNet.RepresentationModel;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// The lists at the doors against what the tree really does (ADR-0057).
/// </summary>
/// <remarks>
/// <para>A caller missing from a list gets a 404, and several callers read that
/// as "nothing there": no exception, no red test, a feature that quietly stops.
/// So the callers are derived from the source and compared with the table here,
/// and a forgotten one falls at build time and not in operation.</para>
/// </remarks>
public sealed class DoorListTests
{
    /// <summary>Who may open which door. The table of ADR-0057.</summary>
    private static readonly Dictionary<string, string[]> Doors = new()
    {
        ["D1"] = ["notification"],
        ["D2"] = ["advisor", "jobs", "transfer"],
        ["D3"] = ["applications", "messages", "transfer"],
        ["D4"] = ["advisor", "companies", "jobs", "messages", "resume", "transfer"],
        ["D5"] = ["advisor", "transfer"],
        ["D6"] = ["applications", "messages", "profile"],
        ["D7"] = ["jobs", "messages", "scout"],
        ["D8"] = ["advisor", "applications", "assessment", "messages", "resume", "scout", "transfer"],
        ["D9"] = ["scout"],
        ["D10"] = ["scout"],
    };

    /// <summary>A door by the path its callers write, with the holes filled by <c>{}</c>.</summary>
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["/internal/notify"] = "D1",
        ["/internal/companies/{}"] = "D2",
        ["/internal/companies/{}/members"] = "D3",
        ["/internal/account/{}/identity"] = "D5",
        ["/internal/account/{}/ai"] = "D6",
        ["/internal/notifications"] = "D8",
        ["/internal/profiles/search?{}"] = "D9",
        ["/internal/profiles/{}"] = "D10",
    };

    private static readonly Regex Literal = new(
        "\"(/internal/[^\"]*)\"", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex Hole = new(
        @"\{[^}]*\}", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex ClientVariable = new(
        "WORKERTRANSFER_M2M_[A-Z]+_SECRET(_HASH)?", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static string Root => RepositoryRoot.Find();

    private static IEnumerable<string> Sources(string service) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src", service + "-service"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The doors a service calls, read out of its source.</summary>
    private static IEnumerable<string> CalledBy(string service)
    {
        var text = string.Join('\n', Sources(service).Select(File.ReadAllText));

        foreach (var match in Literal.Matches(text).Cast<Match>())
        {
            var path = Hole.Replace(match.Groups[1].Value, "{}");

            Paths.Should().ContainKey(path, $"{service} calls a door the table does not know");
            yield return Paths[path];
        }

        // D4 and D7 live in the shared defaults and are switched on per service.
        if (text.Contains("AddCompanyPermissions(", StringComparison.Ordinal)
            || text.Contains("AddAdminPermissions(", StringComparison.Ordinal))
        {
            yield return "D4";
        }

        if (text.Contains("AddCompanyAiAccess(", StringComparison.Ordinal))
        {
            yield return "D7";
        }
    }

    [Fact]
    public void Every_caller_in_the_source_stands_on_the_list_of_its_door_and_the_reverse()
    {
        var derived = RepositoryRoot.Services()
            .SelectMany(service => CalledBy(service).Select(door => (Door: door, Service: service)))
            .Distinct()
            .GroupBy(call => call.Door)
            .ToDictionary(group => group.Key, group => group.Select(call => call.Service).Order().ToArray());

        derived.Should().BeEquivalentTo(Doors);
    }

    [Fact]
    public void Every_caller_of_a_door_is_a_registered_client() =>
        Doors.Values.SelectMany(callers => callers).Distinct()
            .Should().BeEquivalentTo(ServiceClients.All);

    /// <summary>
    /// A client gets a token through <c>AddServiceCall</c> and nothing else, so
    /// the sites of that call are the whole list of clients with a token.
    /// </summary>
    [Fact]
    public void Nothing_sets_a_service_token_past_AddServiceCall()
    {
        var sites = Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(".AddServiceToken(", StringComparison.Ordinal))
            .Select(Path.GetFileName);

        sites.Should().Equal("ServiceCalls.cs");
    }

    private static Dictionary<string, string> Environment(string service)
    {
        using var reader = new StreamReader(Path.Combine(Root, "docker-compose.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);

        var services = (YamlMappingNode)((YamlMappingNode)yaml.Documents[0].RootNode)["services"];

        return Flattened((YamlMappingNode)((YamlMappingNode)services[service + "-service"])["environment"]);
    }

    /// <summary>What a service really gets: its own keys over what it merges in.</summary>
    /// <remarks>
    /// YamlStream keeps <c>&lt;&lt;</c> as an ordinary key. Reading past it leaves the
    /// shared anchor unseen, and a value there reaches every service.
    /// </remarks>
    private static Dictionary<string, string> Flattened(YamlMappingNode mapping)
    {
        var result = mapping.Children.TryGetValue("<<", out var merged)
            ? Flattened((YamlMappingNode)merged)
            : new Dictionary<string, string>();

        foreach (var (key, value) in mapping.Children)
        {
            var name = ((YamlScalarNode)key).Value!;

            if (name != "<<")
            {
                result[name] = ((YamlScalarNode)value).Value ?? string.Empty;
            }
        }

        return result;
    }

    private static string? Value(Dictionary<string, string> environment, string key) =>
        environment.GetValueOrDefault(key);

    public static TheoryData<string> Services() => new(RepositoryRoot.Services());

    /// <summary>Each caller holds its own secret, and nobody else's.</summary>
    [Theory]
    [MemberData(nameof(Services))]
    public void A_service_holds_its_own_secret_and_no_other(string service)
    {
        var environment = Environment(service);
        var secret = Value(environment, "NOELIA_M2M_CLIENT_SECRET");

        if (ServiceClients.All.Contains(service))
        {
            secret.Should().Contain($"WORKERTRANSFER_M2M_{service.ToUpperInvariant()}_SECRET:");
            Value(environment, "NOELIA_M2M_CLIENT_ID").Should().Be(service);
            Value(environment, "NOELIA_M2M_TOKEN_ENDPOINT").Should().Be("http://identity-service:8001/m2m/token");
        }
        else
        {
            // A host named in a service's environment passes its egress boundary.
            secret.Should().BeNull();
            Value(environment, "NOELIA_M2M_TOKEN_ENDPOINT").Should().BeNull();
        }
    }

    /// <summary>The value decides, under whatever key it stands.</summary>
    [Theory]
    [MemberData(nameof(Services))]
    public void No_key_of_a_service_carries_a_client_value_of_another(string service)
    {
        var carried = Environment(service).Values
            .SelectMany(value => ClientVariable.Matches(value).Select(match => match.Value))
            .Distinct();

        IEnumerable<string> expected = service == "identity"
            ? ServiceClients.All.Select(client => $"WORKERTRANSFER_M2M_{client.ToUpperInvariant()}_SECRET_HASH")
            : ServiceClients.All.Contains(service)
                ? [$"WORKERTRANSFER_M2M_{service.ToUpperInvariant()}_SECRET"]
                : [];

        carried.Should().BeEquivalentTo(expected);
    }

    /// <summary>A hash opens nothing, and it stays with the issuer all the same.</summary>
    [Theory]
    [MemberData(nameof(Services))]
    public void Identity_alone_holds_the_hashes(string service)
    {
        var hashes = Environment(service).Keys
            .Where(key => key.EndsWith("_SECRET_HASH", StringComparison.Ordinal))
            .ToList();

        if (service == "identity")
        {
            hashes.Should().BeEquivalentTo(
                ServiceClients.All.Select(client => $"NOELIA_M2M_CLIENT_{client.ToUpperInvariant()}_SECRET_HASH"));
        }
        else
        {
            hashes.Should().BeEmpty();
        }
    }

    [Fact]
    public void The_shared_value_is_gone_from_the_tree()
    {
        string[] old =
        [
            "WORKERTRANSFER_NOTIFY_SECRET", "WORKER_NOTIFY_SECRET", "X-Notify-Secret",
            "Notify__Secret", "Identity__Secret", "Notifications__Secret", "Profile__Secret",
        ];
        string[] files =
        [
            "docker-compose.yml", ".env.example", "Makefile",
            "deploy/helm/workertransfer/values.yaml",
            "deploy/helm/workertransfer/templates/_helpers.tpl",
            "deploy/helm/workertransfer/templates/secret.yaml",
        ];

        var everything = files.Select(file => File.ReadAllText(Path.Combine(Root, file)))
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Select(File.ReadAllText));

        foreach (var text in everything)
        {
            text.Should().NotContainAny(old);
        }
    }

    /// <summary>
    /// Compose and `make env` use the shell recipe, a service checks with Noelia:
    /// a pair that does not fit fails only when the stack starts.
    /// </summary>
    [Fact]
    public async Task The_shell_recipe_makes_a_pair_Noelia_accepts()
    {
        var start = new ProcessStartInfo("bash")
        {
            ArgumentList = { "-c", ". scripts/m2m-pair.sh && m2m_pair" },
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
        };

        using var shell = Process.Start(start)!;
        var lines = (await shell.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await shell.WaitForExitAsync();

        ServiceClientSecret.IsWellFormed(lines[0]).Should().BeTrue();
        lines[1].Should().Be(ServiceClientSecret.HashOf(lines[0]));
    }

    /// <summary>
    /// A file saved without a last newline is ordinary, and its last line may be
    /// somebody's third-party secret. The first key added must not land on it.
    /// </summary>
    [Fact]
    public async Task Adding_the_pairs_leaves_a_file_without_a_last_newline_intact()
    {
        var file = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(file, "THIRD_PARTY_SECRET=keep-me");

            (await AddPairs(file)).Should().Be(0);

            var lines = await File.ReadAllLinesAsync(file);

            lines[0].Should().Be("THIRD_PARTY_SECRET=keep-me");

            foreach (var client in ServiceClients.All.Select(client => client.ToUpperInvariant()))
            {
                lines.Should().ContainSingle(line => line.StartsWith($"WORKERTRANSFER_M2M_{client}_SECRET=", StringComparison.Ordinal));
                lines.Should().ContainSingle(line => line.StartsWith($"WORKERTRANSFER_M2M_{client}_SECRET_HASH=", StringComparison.Ordinal));
            }

            (await AddPairs(file)).Should().Be(0, "a second run finds whole pairs");
            (await File.ReadAllLinesAsync(file)).Should().Equal(lines);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static async Task<int> AddPairs(string file)
    {
        var start = new ProcessStartInfo("bash")
        {
            ArgumentList = { "scripts/env-m2m.sh", file },
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var shell = Process.Start(start)!;
        await Task.WhenAll(shell.StandardOutput.ReadToEndAsync(), shell.StandardError.ReadToEndAsync());
        await shell.WaitForExitAsync();

        return shell.ExitCode;
    }
}
