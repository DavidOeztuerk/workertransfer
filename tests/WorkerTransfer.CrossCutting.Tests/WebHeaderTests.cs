using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// The shipped UI answers every path with its Content-Security-Policy.
/// </summary>
/// <remarks>
/// <para><strong>nginx inherits <c>add_header</c> into a location only when the
/// location sets none of its own.</strong> A policy written once at server level
/// therefore vanishes on exactly the three paths that carry a Cache-Control —
/// <c>index.html</c> among them, the one document a policy has to arrive with.
/// Nothing fails visibly when it is missing: the page works, it is merely
/// unprotected.</para>
///
/// <para>This reads the template the image ships and nothing else, so it runs
/// without an image or a stack.</para>
/// </remarks>
public sealed partial class WebHeaderTests
{
    private const string PolicyLine = "add_header Content-Security-Policy $wt_csp always;";

    private static string Template() =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "docker", "web-nginx.conf.template"));

    [Fact]
    public void Every_location_that_sets_a_header_repeats_the_policy()
    {
        var locations = LocationBlock().Matches(Template()).ToList();

        locations.Should().HaveCountGreaterThanOrEqualTo(4, "the fallback, config.js, index.html and the bundles");
        foreach (var location in locations)
        {
            var body = location.Groups["body"].Value;
            if (body.Contains("add_header", StringComparison.Ordinal))
            {
                body.Should().Contain(PolicyLine, $"`location {location.Groups["path"].Value}` sets a header of its own");
            }
        }
    }

    [Fact]
    public void The_policy_names_the_api_and_lets_nobody_frame_the_ui()
    {
        var policy = PolicyValue().Match(Template()).Groups["policy"].Value;

        policy.Should().Contain("default-src 'self'");
        policy.Should().MatchRegex(@"connect-src 'self' \$\{WT_API_ORIGIN\}");
        policy.Should().Contain("frame-ancestors 'none'");
        policy.Should().Contain("object-src 'none'");
        policy.Should().NotContain("'unsafe-eval'");
        policy.Should().NotMatchRegex(@"script-src[^;]*'unsafe-inline'", "an inline script would be the attacker's");
    }

    /// <summary>
    /// Only the drive.file picker and the chooser may load script — never a
    /// whole provider domain behind a wildcard (ADR-0054).
    /// </summary>
    [Fact]
    public void Script_comes_from_the_bundle_and_the_two_pickers_only()
    {
        var policy = PolicyValue().Match(Template()).Groups["policy"].Value;
        var scripts = ScriptSources().Match(policy).Groups["sources"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        scripts.Should().BeEquivalentTo(
            "'self'", "https://apis.google.com", "https://accounts.google.com", "https://www.dropbox.com");
    }

    [Fact]
    public void The_image_gives_the_api_origin_a_value()
    {
        var dockerfile = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "docker", "web-prod.Dockerfile"));

        dockerfile.Should().Contain("/etc/nginx/templates/default.conf.template",
            "only a template under /etc/nginx/templates/ is substituted at start");
        dockerfile.Should().MatchRegex(@"ENV WT_API_ORIGIN=\S+",
            "without a value the policy names a literal ${WT_API_ORIGIN} and refuses every API call");
    }

    [GeneratedRegex(@"location\s+(?<path>[^{]+)\{(?<body>[^}]*)\}")]
    private static partial Regex LocationBlock();

    [GeneratedRegex(@"map \$host \$wt_csp \{\s*default ""(?<policy>[^""]+)"";")]
    private static partial Regex PolicyValue();

    [GeneratedRegex(@"script-src (?<sources>[^;]+);")]
    private static partial Regex ScriptSources();
}
