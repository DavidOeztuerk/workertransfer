using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noelia.Abstractions.Security.Checks;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>What each service declares about its model use, and what Noelia makes of it.</summary>
/// <remarks>
/// <para>Noelia 7.0.0 no longer reads "no model host recognised" as "no
/// model": recognition is by host name, and here the endpoints stand per person
/// in <c>AiAccessV1</c>. Without a declaration <c>noelia.ai.inventory</c> and
/// <c>noelia.ai.record-keeping</c> both say "not determined" — correct, and
/// misleading for the nine services that hold nothing of the kind.</para>
///
/// <para>Two halves, because one could not catch the other: the source pins
/// <em>which</em> service says what, the composition pins what the saying does.
/// A declaration that is registered and changes no check would be the
/// "registered, present, without effect" class ADR-0044 was built against.</para>
/// </remarks>
public sealed class ArtificialIntelligenceDeclarationTests
{
    /// <summary>The services that call a model or hold a person's AI access.</summary>
    /// <remarks>
    /// identity holds the access (<c>AiAccessV1</c>), applications writes the
    /// cover letter with it, profile, jobs and scout draft through
    /// <c>IDrafter</c>. assessment has no drafter by decision (ADR-0042), and
    /// resume's recogniser is a library in-process, not a model (ADR-0043).
    /// </remarks>
    private static readonly string[] Using =
        ["applications", "identity", "jobs", "messages", "profile", "scout"];

    private static IReadOnlyDictionary<string, string> Declarations()
    {
        var declarations = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot.Find(), "src"), "Program.cs", SearchOption.AllDirectories))
        {
            if (entry.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || entry.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || entry.Contains($"{Path.DirectorySeparatorChar}gateway{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var service = Path.GetRelativePath(Path.Combine(RepositoryRoot.Find(), "src"), entry)
                .Split(Path.DirectorySeparatorChar)[0]
                .Replace("-service", string.Empty, StringComparison.Ordinal);

            var found = Regex.Matches(
                File.ReadAllText(entry), @"ArtificialIntelligence\.(?<value>Used|NotUsed)\b",
                RegexOptions.None, TimeSpan.FromSeconds(5));

            found.Should().HaveCount(1, $"'{service}' states its model use exactly once");
            declarations[service] = found[0].Groups["value"].Value;
        }

        return declarations;
    }

    [Fact]
    public void Every_service_declares_and_exactly_these_say_yes()
    {
        var declarations = Declarations();

        declarations.Should().HaveCount(15, "fifteen services, the gateway calls no defaults");
        declarations.Where(pair => pair.Value == "Used").Select(pair => pair.Key)
            .Should().BeEquivalentTo(Using);
    }

    [Fact]
    public void A_forgotten_declaration_is_refused_not_read_as_no()
    {
        var act = () => Compose(default);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*states its model use*");
    }

    /// <summary>"Yes" without an endpoint: the inventory says the endpoint is not recognisable.</summary>
    [Fact]
    public async Task A_service_that_uses_a_model_is_not_reported_as_without_one()
    {
        var inventory = await RunAsync(ArtificialIntelligence.Used, "noelia.ai.inventory");

        inventory.Status.Should().Be(SecurityCheckStatus.Warning);
        inventory.Summary.Should().Contain("declared that this service uses a model")
            .And.Contain("not recognisable");

        // The sink question is asked, not answered away as "not determined".
        var records = await RunAsync(ArtificialIntelligence.Used, "noelia.ai.record-keeping");

        records.Summary.Should().NotContain("Not determined")
            .And.Contain("audit sink");
    }

    /// <summary>"No": not applicable, and said to be a declaration rather than an observation.</summary>
    [Fact]
    public async Task A_service_without_a_model_says_so_as_a_declaration()
    {
        foreach (var id in new[] { "noelia.ai.inventory", "noelia.ai.record-keeping" })
        {
            var result = await RunAsync(ArtificialIntelligence.NotUsed, id);

            result.Status.Should().Be(SecurityCheckStatus.NotApplicable, id);
            result.Summary.Should().Contain("declaration, not an observation", id);
        }
    }

    private static async Task<SecurityCheckResult> RunAsync(
        ArtificialIntelligence declaration, string id)
    {
        await using var provider = Compose(declaration).BuildServiceProvider();

        var check = provider.GetServices<ISecurityCheck>().Single(check => check.Id == id);

        return await check.RunAsync();
    }

    /// <summary>The shared composition root, with no model host in the configuration.</summary>
    private static IServiceCollection Compose(ArtificialIntelligence declaration)
    {
        var host = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true
        });

        host.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:KeyId"] = "test",
            ["Jwt:PublicKey"] =
                "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==",
            ["JwtSettings:Issuer"] = "workertransfer",
            ["JwtSettings:Audience"] = "workertransfer",
            ["Consent:BaseUrl"] = "http://consent-service:8002"
        });

        return host.Services.AddWorkerTransferDefaults(
            host.Configuration, host.Environment, "declaration-test", declaration);
    }
}
