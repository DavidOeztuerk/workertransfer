using System.Reflection;
using FluentAssertions;
using YamlDotNet.RepresentationModel;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// Every configuration key compose sets for a service reaches a settings
/// property of that service.
/// </summary>
/// <remarks>
/// <para><strong>A settings property name IS the environment key.</strong>
/// <c>LookupSettings.Resume</c> binds <c>Lookup__Resume</c>; rename the
/// property alone and the value stays on its default, silently. Measured twice
/// in the English rename, while the keys were still German:
/// <c>GitHub__OAuth__Rueckadresse</c> after the property became
/// <c>RedirectUrl</c>, and <c>Auskunft__Resume</c> after <c>Resume</c> became
/// <c>CurriculumVitae</c>. The build was green, and so was every suite — each
/// test host sets its own keys or replaces the component with a probe, so none
/// of them read what compose sets. The keys were renamed afterwards in one pass
/// over code, compose, the chart and <c>.env.example</c>.</para>
///
/// <para>This test reads compose and the settings types, and nothing in
/// between: a key must land on a property of a type whose <c>Section</c> is
/// the key's prefix. A key from the service's own block must land in that
/// service (or in <c>src/shared</c>); a key from the shared anchor must land in
/// at least one service.</para>
/// </remarks>
public sealed class ComposeKeyTests
{
    /// <summary>Sections that belong to Noelia or ASP.NET, not to a settings type of ours.</summary>
    private static readonly string[] ForeignSections =
        ["ConnectionStrings", "Jwt", "JwtSettings", "DistributedRateLimiting", "Dashboard"];

    /// <summary>Keys read by name elsewhere, each with the place that reads it.</summary>
    /// <remarks>
    /// <c>Draft:AllowedTargets</c> has no property on purpose: the egress
    /// boundary (<c>ServiceFoundation</c>) takes the host from any configuration
    /// value that reads as an absolute http URL, and these values exist only to
    /// be read that way.
    /// </remarks>
    private static readonly string[] ReadByName = ["Draft:AllowedTargets"];

    /// <summary>The services compose starts from the one image, by <c>SERVICE_DIR</c>.</summary>
    public static TheoryData<string> Services
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var service in ServiceKeys().Keys.Where(service => service != "gateway"))
            {
                data.Add(service);
            }

            return data;
        }
    }

    [Fact]
    public void Compose_is_read_at_all()
    {
        ServiceKeys().Should().HaveCountGreaterThan(
            10, "otherwise this test reads no service and checks nothing");

        SharedKeys().Should().Contain(
            "Erasure__Secret", "otherwise the shared anchor was not found");

        SettingsTypes().Should().HaveCountGreaterThan(
            20, "otherwise the settings types were not found and every key looks unowned");
    }

    [Theory]
    [MemberData(nameof(Services))]
    public void Every_key_of_a_service_reaches_a_property(string service)
    {
        var owners = SettingsTypes()
            .Where(type => BelongsTo(type, service) || IsShared(type))
            .ToList();

        foreach (var key in ServiceKeys()[service].Where(IsOurs))
        {
            Lands(key, owners).Should().BeTrue(
                $"compose sets '{key}' for {service}-service, and no settings property "
                + "there has that name — the value stays on its default, silently");
        }
    }

    [Fact]
    public void Every_key_of_the_shared_anchor_reaches_a_property()
    {
        var owners = SettingsTypes();

        foreach (var key in SharedKeys().Where(IsOurs))
        {
            Lands(key, owners).Should().BeTrue(
                $"the shared anchor sets '{key}', and no service has a settings property "
                + "with that name");
        }
    }

    /// <summary>The counter-probe: the rule really refuses a renamed property.</summary>
    [Fact]
    public void A_renamed_property_is_found()
    {
        var owners = SettingsTypes();

        Lands("Lookup__Resume", owners).Should().BeTrue();
        Lands("Lookup__CurriculumVitae", owners).Should().BeFalse();
        Lands("GitHub__OAuth__RedirectUrl", owners).Should().BeTrue();

        // The German keys are gone, not kept beside the English ones: a second
        // name for the same setting is a second path.
        Lands("GitHub__OAuth__Rueckadresse", owners).Should().BeFalse();
        Lands("Consent__Adresse", owners).Should().BeFalse();
        Lands("Auskunft__Resume", owners).Should().BeFalse();
    }

    // ---------------------------------------------------------------------

    private static bool IsOurs(string key) =>
        key.Contains("__", StringComparison.Ordinal)
        && !ForeignSections.Contains(key.Split("__")[0], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a key binds a property of one of the owners.</summary>
    /// <remarks>
    /// The longest prefix that some type claims as its <c>Section</c> decides;
    /// what follows the property (a dictionary key, an array index) is the
    /// property's own business.
    /// </remarks>
    private static bool Lands(string key, IReadOnlyList<Type> owners)
    {
        var parts = key.Split("__");

        for (var length = parts.Length - 1; length >= 1; length--)
        {
            var section = string.Join(':', parts[..length]);
            var claiming = owners.Where(type => string.Equals(
                Section(type), section, StringComparison.OrdinalIgnoreCase)).ToList();

            if (claiming.Count == 0)
            {
                continue;
            }

            var property = parts[length];

            return claiming.Any(type => type.GetProperty(
                       property,
                       BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is not null)
                   || ReadByName.Contains($"{section}:{property}", StringComparer.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string? Section(Type type) =>
        type.GetField("Section", BindingFlags.Public | BindingFlags.Static) is
        { IsLiteral: true } field
            ? field.GetRawConstantValue() as string
            : null;

    private static bool BelongsTo(Type type, string service) =>
        type.Assembly.GetName().Name is { } name
        && name.StartsWith($"WorkerTransfer.{service}.", StringComparison.OrdinalIgnoreCase);

    private static bool IsShared(Type type) =>
        type.Assembly.GetName().Name is { } name
        && !ServiceKeys().Keys.Any(service =>
            name.StartsWith($"WorkerTransfer.{service}.", StringComparison.OrdinalIgnoreCase));

    /// <summary>Every type of ours that declares a <c>Section</c> constant.</summary>
    private static List<Type> SettingsTypes() =>
        [.. Directory.EnumerateFiles(AppContext.BaseDirectory, "WorkerTransfer.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom)
            .SelectMany(Types)
            .Where(type => Section(type) is not null)];

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            return error.Types.OfType<Type>();
        }
    }

    // ---------------------------------------------------------------------

    private static YamlMappingNode Compose()
    {
        var stream = new YamlStream();

        using var reader = new StreamReader(
            Path.Combine(RepositoryRoot.Find(), "docker-compose.yml"));
        stream.Load(reader);

        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    /// <summary>The keys in the shared anchor (<c>x-environment</c>).</summary>
    private static List<string> SharedKeys() =>
        [.. ((YamlMappingNode)Compose().Children[new YamlScalarNode("x-environment")]).Children.Keys
            .Select(key => ((YamlScalarNode)key).Value!)];

    /// <summary>
    /// Each service's own keys, by <c>SERVICE_DIR</c> — without the shared
    /// anchor it merges in.
    /// </summary>
    private static Dictionary<string, List<string>> ServiceKeys()
    {
        var services = (YamlMappingNode)Compose().Children[new YamlScalarNode("services")];
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (_, node) in services.Children)
        {
            if (node is not YamlMappingNode service
                || !service.Children.TryGetValue(new YamlScalarNode("environment"), out var environment)
                || environment is not YamlMappingNode variables
                || !variables.Children.TryGetValue(new YamlScalarNode("SERVICE_DIR"), out var directory))
            {
                continue;
            }

            result[((YamlScalarNode)directory).Value!] =
                [.. variables.Children.Keys
                    .Select(key => ((YamlScalarNode)key).Value!)
                    .Where(key => key != "<<")];
        }

        return result;
    }
}
