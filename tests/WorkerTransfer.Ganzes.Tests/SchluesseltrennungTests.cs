using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

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
public sealed class SchluesseltrennungTests
{
    private static string Compose() =>
        File.ReadAllText(Path.Combine(Postgres_Ersatz2.Repowurzel(), "docker-compose.yml"));

    /// <summary>Which compose services carry an environment key.</summary>
    /// <remarks>
    /// Comment lines do not count: the reason beside a key names it, and a test
    /// that counts that along measures its own comment.
    /// </remarks>
    private static IReadOnlyList<string> TraegerVon(string key)
    {
        var traeger = new List<string>();
        var oben = string.Empty;
        var service = string.Empty;

        foreach (var row in Compose().Split('\n'))
        {
            var block = Regex.Match(
                row, @"^(?<einzug> {0,2})(?<name>[a-zA-Z0-9_-]+):\s*(?:&[A-Za-z0-9_-]+\s*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(5));

            if (block.Success)
            {
                if (block.Groups["einzug"].Value.Length == 0)
                {
                    oben = block.Groups["name"].Value;
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
                // anchor itself — `x-umgebung` has no second level.
                traeger.Add(oben == "services" ? service : oben);
            }
        }

        return traeger;
    }

    [Fact]
    public void Only_identity_service_holds_the_private_key()
        => TraegerVon("Jwt__PrivateKey").Should().Equal(
            ["identity-service"],
            "ein zweiter Aussteller wäre eine zweite Stelle, die einen "
            + "Handelnden erschaffen kann");

    /// <summary>And the counter-probe: the public one stands in the shared anchor.</summary>
    /// <remarks>
    /// Without it, it would stay open whether the private key stands in one
    /// place only — or whether this test simply finds nothing. The anchor is a
    /// block of its own and therefore carries no service name.
    /// </remarks>
    [Fact]
    public void The_public_key_stands_exactly_once_and_in_the_anchor()
        => TraegerVon("Jwt__PublicKey").Should().Equal(
            ["x-umgebung"],
            "er gilt für alle fünfzehn und gehört deshalb in den gemeinsamen Anker");

    /// <summary>The shared secret is gone, and gone everywhere.</summary>
    [Fact]
    public void No_service_gets_a_shared_signing_secret_any_more()
        => TraegerVon("JwtSettings__Secret").Should().BeEmpty(
            "ein Geheimnis, mit dem man prüfen kann, kann auch prägen");
}
