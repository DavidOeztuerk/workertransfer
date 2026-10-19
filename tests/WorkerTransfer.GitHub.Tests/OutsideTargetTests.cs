using System.Text.RegularExpressions;
using FluentAssertions;
using WorkerTransfer.GitHub.Infrastructure.Network;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Both hosts this service calls outside stand where the egress boundary reads.</summary>
/// <remarks>
/// <para>The boundary allows only hosts out of the configuration. A default in
/// the source is invisible to it: measured 01.10.2026, the running
/// github-service allowed <c>api.github.com</c> but not <c>github.com</c>, so
/// the OAuth code exchange was refused and every sign-in read "github is
/// unreachable". The chart did not even name <c>api.github.com</c>.</para>
///
/// <para>The expected hosts come from the settings' own defaults, so a moved
/// default turns this red instead of drifting from the deployment.</para>
/// </remarks>
public sealed class OutsideTargetTests
{
    private static string Root()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !File.Exists(Path.Combine(root.FullName, "WorkerTransfer.slnx")))
        {
            root = root.Parent;
        }

        return root?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    public static TheoryData<string, string> Targets => new()
    {
        { "GitHub__BaseUrl", new Uri(new GitHubSettings().BaseUrl).Host },
        { "GitHub__OAuth__AuthorizeUrl", new Uri(new GitHubSignInSettings().AuthorizeUrl).Host },
    };

    [Theory]
    [MemberData(nameof(Targets))]
    public void Compose_names_the_target(string key, string host) =>
        HostIn(Block("docker-compose.yml", @"^  github-service:\s*$", @"^  [a-z-]+:\s*$"), key)
            .Should().Be(host, $"{key} in compose's github-service block is what the boundary allows");

    [Theory]
    [MemberData(nameof(Targets))]
    public void The_chart_names_the_target(string key, string host) =>
        HostIn(
                Block(
                    Path.Combine("deploy", "helm", "workertransfer", "values.yaml"),
                    @"^  - name: github-service\s*$",
                    @"^  - name: "),
                key)
            .Should().Be(host, $"{key} in the chart's github-service entry is what the boundary allows");

    /// <summary>The lines of one service's block, comments dropped.</summary>
    private static string Block(string file, string start, string next)
    {
        var lines = File.ReadAllLines(Path.Combine(Root(), file));
        var from = Array.FindIndex(lines, line => Regex.IsMatch(line, start));

        from.Should().BeGreaterThanOrEqualTo(0, $"{file} has a github-service block");

        var block = lines.Skip(from + 1)
            .TakeWhile(line => !Regex.IsMatch(line, next))
            .Where(line => !line.TrimStart().StartsWith('#'));

        return string.Join('\n', block);
    }

    private static string? HostIn(string block, string key)
    {
        var match = Regex.Match(block, $@"^\s*{Regex.Escape(key)}:\s*(?<value>\S+)\s*$", RegexOptions.Multiline);

        return match.Success && Uri.TryCreate(match.Groups["value"].Value, UriKind.Absolute, out var address)
            ? address.Host
            : null;
    }
}
