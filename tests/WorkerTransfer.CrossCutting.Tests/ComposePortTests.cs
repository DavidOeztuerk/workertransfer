using FluentAssertions;
using YamlDotNet.RepresentationModel;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>Only the gateway and the UI listen beyond this machine.</summary>
/// <remarks>
/// <para>A port published as <c>"8001:8001"</c> listens on every interface of
/// the host. Anybody in the same LAN then reached <c>/internal/*</c>,
/// <c>/noelia</c> and <c>/auth/login</c> past the gateway and its brake, and
/// Postgres with the development password (ADR-0056).</para>
///
/// <para>Bound to <c>127.0.0.1</c> the host still reaches every service — the
/// control plane on the host collects through exactly these ports — and the LAN
/// reaches only the two doors that are meant to be open.</para>
/// </remarks>
public sealed class ComposePortTests
{
    /// <summary>The doors meant to be open: the API's single entrance and the UI.</summary>
    private static readonly string[] Public = ["gateway", "web"];

    [Fact]
    public void Every_other_published_port_binds_to_loopback()
    {
        var published = PublishedPorts();

        // A test that sees no ports at all would be green for the wrong reason.
        published.Should().Contain(port => port.Service == "postgres");

        published
            .Where(port => !Public.Contains(port.Service))
            .Where(port => !port.Mapping.StartsWith("127.0.0.1:", StringComparison.Ordinal))
            .Select(port => $"{port.Service}: {port.Mapping}")
            .Should().BeEmpty("only the gateway and the UI may listen beyond loopback (ADR-0056)");
    }

    // ---------------------------------------------------------------------

    /// <summary>Every published port, as compose spells it, with its service.</summary>
    /// <remarks>
    /// The long syntax is read as <c>host_ip:published:target</c>, so a mapping
    /// without <c>host_ip</c> fails the same way the short one does.
    /// </remarks>
    private static List<(string Service, string Mapping)> PublishedPorts()
    {
        var stream = new YamlStream();

        using (var reader = new StreamReader(Path.Combine(RepositoryRoot.Find(), "docker-compose.yml")))
        {
            stream.Load(reader);
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;
        var services = (YamlMappingNode)root.Children[new YamlScalarNode("services")];
        var published = new List<(string, string)>();

        foreach (var (name, body) in services.Children)
        {
            if (body is not YamlMappingNode service
                || !service.Children.TryGetValue(new YamlScalarNode("ports"), out var ports))
            {
                continue;
            }

            foreach (var port in (YamlSequenceNode)ports)
            {
                published.Add((((YamlScalarNode)name).Value!, port switch
                {
                    YamlScalarNode scalar => scalar.Value!,
                    YamlMappingNode mapping => $"{Value(mapping, "host_ip")}:{Value(mapping, "published")}:{Value(mapping, "target")}",
                    _ => port.ToString(),
                }));
            }
        }

        return published;
    }

    private static string Value(YamlMappingNode mapping, string key) =>
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var node) ? ((YamlScalarNode)node).Value ?? "" : "";
}
