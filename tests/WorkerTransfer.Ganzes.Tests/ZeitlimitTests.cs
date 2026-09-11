using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Jeder ausgehende Aufruf setzt ein Zeitlimit.
/// </summary>
/// <remarks>
/// <para><strong>Warum das überhaupt ein Test ist.</strong> Ohne eigene Zeile
/// gilt die Vorgabe von <c>HttpClient</c>: <strong>hundert Sekunden</strong>.
/// Das liest sich wie „es gibt ein Zeitlimit" und ist in einem
/// Dienst-zu-Dienst-Aufruf keins — ein hängender Dienst hängt den Aufrufer mit,
/// und der hängt den Menschen davor mit. Gemessen waren sieben von fünfzehn
/// Aufrufstellen ohne, darunter ein <em>Einwilligungstor</em>, während alle
/// anderen Einwilligungstore eines hatten. Das war kein Entwurf, das war ein
/// Vergessen.</para>
///
/// <para><strong>Über den Quelltext und nicht über die Verdrahtung</strong>,
/// weil genau das die Frage ist: Vergessen sieht man einer laufenden Anwendung
/// nicht an. Sie antwortet ja — nur eben irgendwann.</para>
///
/// <para>Ein Zeitlimit je Aufrufstelle und nicht eines für alle: fünf Sekunden
/// sind für eine Einwilligungsprüfung großzügig und für einen Entwurf über ein
/// Sprachmodell zu wenig. Eine Zahl für beides wäre für eine der beiden
/// falsch.</para>
///
/// <para><strong>Gesucht wird die ZUWEISUNG, nicht der Variablenname.</strong>
/// Hier stand <c>client.Timeout</c>, und damit hing die Zusage daran, wie
/// jemand seine lokale Variable nennt: <c>HttpFirmenrollen</c> setzt sein
/// Zeitlimit an <c>klient</c> und fiel als „ohne Zeitlimit" durch, obwohl es
/// eines hat. Ein Waechter, der bei richtigem Code rot wird, wird beim nächsten
/// Mal weggeschaltet statt gelesen.</para>
/// </remarks>
public sealed class ZeitlimitTests
{
    private static IEnumerable<string> Aufrufstellen() =>
        Directory.EnumerateFiles(
                Path.Combine(Postgres_Ersatz2.Repowurzel(), "src"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(pfad => !pfad.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                   && !pfad.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal))
            .Where(pfad => File.ReadAllText(pfad)
                .Contains("fabrik.CreateClient(", StringComparison.Ordinal));

    [Fact]
    public void Jede_Aufrufstelle_setzt_ein_Zeitlimit()
    {
        var stellen = Aufrufstellen().ToList();

        stellen.Should().HaveCountGreaterThan(
            10, "sonst findet dieser Test die Aufrufstellen gar nicht mehr");

        foreach (var pfad in stellen)
        {
            File.ReadAllText(pfad).Should().Contain(
                ".Timeout =",
                $"'{Path.GetFileName(pfad)}' ruft einen anderen Dienst, setzt aber kein "
                + "Zeitlimit — dann gilt die Vorgabe von hundert Sekunden");
        }
    }

    /// <summary>
    /// Und keine Aufrufstelle wartet länger als eine Minute.
    /// </summary>
    /// <remarks>
    /// Die Obergrenze ist bewusst weit: sie soll nicht die Wahl zwischen fünf
    /// und dreißig Sekunden vorschreiben, sondern verhindern, dass jemand
    /// versehentlich in die Nähe der hundert Sekunden zurückrutscht.
    /// </remarks>
    [Fact]
    public void Keine_Aufrufstelle_wartet_laenger_als_eine_Minute()
    {
        var muster = new Regex(
            @"Zeitueberschreitung \{ get; set; \} = TimeSpan\.From(?<einheit>Seconds|Minutes)\((?<zahl>[\d.]+)\)",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        var quellen = Directory.EnumerateFiles(
                Path.Combine(Postgres_Ersatz2.Repowurzel(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(pfad => !pfad.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !pfad.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var gefunden = 0;

        foreach (var pfad in quellen)
        {
            foreach (Match treffer in muster.Matches(File.ReadAllText(pfad)))
            {
                gefunden++;
                var zahl = double.Parse(treffer.Groups["zahl"].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                var spanne = treffer.Groups["einheit"].Value == "Minutes"
                    ? TimeSpan.FromMinutes(zahl)
                    : TimeSpan.FromSeconds(zahl);

                spanne.Should().BeLessThanOrEqualTo(
                    TimeSpan.FromMinutes(1),
                    $"'{Path.GetFileName(pfad)}' wartet zu lange");
            }
        }

        gefunden.Should().BeGreaterThan(10, "sonst prueft dieser Test nichts");
    }
}

/// <summary>Findet die Repowurzel.</summary>
internal static class Postgres_Ersatz2
{
    public static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "WorkerTransfer.slnx")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
               ?? throw new InvalidOperationException("Repowurzel nicht gefunden.");
    }
}
