using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>Wer Token prägen kann, und wer sie nur liest.</summary>
/// <remarks>
/// <para>Mit einem geteilten HS256-Geheimnis gab es diesen Unterschied nicht:
/// jeder der fünfzehn Prozesse konnte ein Token für jeden Menschen ausstellen,
/// und ein kopiertes Konfigurationsblatt wäre jedes Konto auf jedem Dienst
/// gewesen.</para>
///
/// <para>Der Unterschied steht jetzt in <c>docker-compose.yml</c> und nicht im
/// Code — deshalb prüft ihn ein Test, der die Datei liest. Ein zweiter Träger
/// des privaten Schlüssels wäre eine zweite Stelle, die einen Handelnden
/// erschaffen kann, und nichts weiter unten könnte die beiden
/// auseinanderhalten.</para>
/// </remarks>
public sealed class SchluesseltrennungTests
{
    private static string Compose() =>
        File.ReadAllText(Path.Combine(Postgres_Ersatz2.Repowurzel(), "docker-compose.yml"));

    /// <summary>Welche compose-Dienste einen Umgebungsschlüssel tragen.</summary>
    /// <remarks>
    /// Kommentarzeilen zählen nicht: die Begründung neben einem Schlüssel nennt
    /// ihn beim Namen, und ein Test, der das mitzählt, misst seinen eigenen
    /// Kommentar.
    /// </remarks>
    private static IReadOnlyList<string> TraegerVon(string schluessel)
    {
        var traeger = new List<string>();
        var oben = string.Empty;
        var dienst = string.Empty;

        foreach (var zeile in Compose().Split('\n'))
        {
            var block = Regex.Match(
                zeile, @"^(?<einzug> {0,2})(?<name>[a-zA-Z0-9_-]+):\s*(?:&[A-Za-z0-9_-]+\s*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(5));

            if (block.Success)
            {
                if (block.Groups["einzug"].Value.Length == 0)
                {
                    oben = block.Groups["name"].Value;
                    dienst = string.Empty;
                }
                else
                {
                    dienst = block.Groups["name"].Value;
                }

                continue;
            }

            if (!zeile.TrimStart().StartsWith('#')
                && zeile.Contains($"{schluessel}:", StringComparison.Ordinal))
            {
                // Unter `services:` ist der Eigentuemer der Dienst, sonst der
                // Anker selbst — `x-umgebung` hat keine zweite Ebene.
                traeger.Add(oben == "services" ? dienst : oben);
            }
        }

        return traeger;
    }

    [Fact]
    public void Nur_identity_service_haelt_den_privaten_Schluessel()
        => TraegerVon("Jwt__PrivateKey").Should().Equal(
            ["identity-service"],
            "ein zweiter Aussteller wäre eine zweite Stelle, die einen "
            + "Handelnden erschaffen kann");

    /// <summary>Und die Gegenprobe: der öffentliche steht im gemeinsamen Anker.</summary>
    /// <remarks>
    /// Ohne sie bliebe offen, ob der private Schlüssel nur an einer Stelle
    /// steht — oder ob dieser Test einfach nichts findet. Der Anker ist ein
    /// eigener Block und trägt deshalb keinen Dienstnamen.
    /// </remarks>
    [Fact]
    public void Der_oeffentliche_Schluessel_steht_genau_einmal_und_im_Anker()
        => TraegerVon("Jwt__PublicKey").Should().Equal(
            ["x-umgebung"],
            "er gilt für alle fünfzehn und gehört deshalb in den gemeinsamen Anker");

    /// <summary>Das geteilte Geheimnis ist weg, und zwar überall.</summary>
    [Fact]
    public void Kein_Dienst_bekommt_mehr_ein_geteiltes_Signaturgeheimnis()
        => TraegerVon("JwtSettings__Secret").Should().BeEmpty(
            "ein Geheimnis, mit dem man prüfen kann, kann auch prägen");
}
