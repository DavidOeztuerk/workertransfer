using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>Who can mint tokens, and who only reads them.</summary>
/// <remarks>
/// <para>With a shared HS256 secret this difference did not exist: any one of
/// the fifteen processes could issue a token for any human being, and a copied
/// configuration sheet would have been every account on every service.</para>
///
/// <para>The difference now stands in <c>docker-compose.yml</c> and not in the
/// code — which is why a test that reads the file checks it. A second carrier of
/// the private key would be a second place that can create an acting principal,
/// and nothing further down could tell the two apart.</para>
/// </remarks>
public sealed class KeySeparationTests
{
    private static string Compose() =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Find(), "docker-compose.yml"));

    /// <summary>Which compose services carry an environment key.</summary>
    /// <remarks>
    /// Comment lines do not count: the reason beside a key names it, and a test
    /// that counts that along measures its own comment.
    /// </remarks>
    private static IReadOnlyList<string> CarriersOf(string key)
    {
        var carriers = new List<string>();
        var topLevel = string.Empty;
        var service = string.Empty;

        foreach (var row in Compose().Split('\n'))
        {
            var block = Regex.Match(
                row, @"^(?<indent> {0,2})(?<name>[a-zA-Z0-9_-]+):\s*(?:&[A-Za-z0-9_-]+\s*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(5));

            if (block.Success)
            {
                if (block.Groups["indent"].Value.Length == 0)
                {
                    topLevel = block.Groups["name"].Value;
                    service = string.Empty;
                }
                else
                {
                    service = block.Groups["name"].Value;
                }

                continue;
            }

            if (!row.TrimStart().StartsWith('#')
                && row.Contains($"{key}:", StringComparison.Ordinal))
            {
                // Under `services:` the owner is the service, otherwise the
                // anchor itself — `x-environment` has no second level.
                carriers.Add(topLevel == "services" ? service : topLevel);
            }
        }

        return carriers;
    }

    [Fact]
    public void Only_identity_service_holds_the_private_key()
        => CarriersOf("Jwt__PrivateKey").Should().Equal(
            ["identity-service"],
            "a second issuer would be a second place that can create an "
            + "acting principal");

    /// <summary>And the counter-probe: the public one stands in the shared anchor.</summary>
    /// <remarks>
    /// Without it, it would stay open whether the private key stands in one
    /// place only — or whether this test simply finds nothing. The anchor is a
    /// block of its own and therefore carries no service name.
    /// </remarks>
    [Fact]
    public void The_public_key_stands_exactly_once_and_in_the_anchor()
        => CarriersOf("Jwt__PublicKey").Should().Equal(
            ["x-environment"],
            "it applies to all fifteen and therefore belongs in the shared anchor");

    /// <summary>The shared secret is gone, and gone everywhere.</summary>
    [Fact]
    public void No_service_gets_a_shared_signing_secret_any_more()
        => CarriersOf("JwtSettings__Secret").Should().BeEmpty(
            "a secret that can verify can also mint");
}
