using System.Text.RegularExpressions;
using FluentAssertions;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// <c>.env.example</c> ist vollständig — und trägt kein Geheimnis.
/// </summary>
/// <remarks>
/// <para>Eine Vorlage, die einen Schlüssel nicht kennt, ist schlimmer als
/// keine: sie sieht vollständig aus. Wer sie kopiert, bekommt einen Stapel, der
/// beim Start abbricht — und sucht den Fehler bei sich.</para>
///
/// <para>Geprüft wird gegen <c>docker-compose.yml</c>, weil dort steht, was
/// wirklich gebraucht wird. Beide Richtungen: kein <c>${…}</c> ohne Eintrag in
/// der Vorlage, und kein Eintrag in der Vorlage, den niemand liest.</para>
/// </remarks>
public sealed class UmgebungTests
{
    private static string Wurzel() => Postgres_Ersatz2.Repowurzel();

    /// <summary>Die Namen, die docker-compose.yml aus der Umgebung erwartet.</summary>
    private static IReadOnlyList<string> AusCompose()
    {
        var roh = File.ReadAllText(Path.Combine(Wurzel(), "docker-compose.yml"));

        // Kommentarzeilen raus: dort steht die Begruendung, warum ein frueherer
        // Vorgabewert weg ist — samt seiner Schreibweise.
        var ohneKommentar = string.Join(
            "\n",
            roh.Split('\n').Where(zeile => !zeile.TrimStart().StartsWith('#')));

        return [.. Regex.Matches(
                ohneKommentar,
                @"\$\{(?<name>WORKERTRANSFER_[A-Z_]+|ANTHROPIC_API_KEY)[:}]",
                RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(treffer => treffer.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    /// <summary>Die Namen, die die Vorlage setzt.</summary>
    private static IReadOnlyList<string> AusVorlage() =>
        [.. File.ReadAllLines(Path.Combine(Wurzel(), ".env.example"))
            .Where(zeile => !zeile.TrimStart().StartsWith('#'))
            .Select(zeile => zeile.Split('=', 2))
            .Where(teile => teile.Length == 2 && teile[0].Trim().Length > 0)
            .Select(teile => teile[0].Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void Jeder_Schluessel_aus_compose_steht_in_der_Vorlage()
    {
        var gebraucht = AusCompose();

        gebraucht.Should().NotBeEmpty("sonst findet dieser Test die Schluessel gar nicht");
        AusVorlage().Should().Contain(
            gebraucht,
            ".env.example muss jeden Schluessel kennen, den docker-compose.yml erwartet");
    }

    /// <summary>
    /// Die drei Geheimnisse stehen in der Vorlage <strong>leer</strong>.
    /// </summary>
    /// <remarks>
    /// Der Kern von H2: ein eingebauter Vorgabewert <em>ist</em> das Geheimnis,
    /// und er liegt damit in git. <c>make env</c> würfelt sie beim Anlegen.
    /// </remarks>
    [Theory]
    [InlineData("WORKERTRANSFER_JWT_SECRET")]
    [InlineData("WORKERTRANSFER_NOTIFY_SECRET")]
    [InlineData("WORKERTRANSFER_ERASURE_SECRET")]
    public void Kein_Geheimnis_traegt_einen_Wert(string name)
    {
        var zeile = File.ReadAllLines(Path.Combine(Wurzel(), ".env.example"))
            .Single(z => z.StartsWith($"{name}=", StringComparison.Ordinal));

        zeile.Should().Be(
            $"{name}=",
            "ein Vorgabewert waere das Geheimnis selbst, und es laege in git");
    }

    /// <summary>
    /// Und in <c>docker-compose.yml</c> hat keines eine Vorgabe.
    /// </summary>
    /// <remarks>
    /// Die andere Haelfte derselben Zusage. <c>${X:-vorgabe}</c> waere genau das
    /// eingebaute Geheimnis; <c>${X:?…}</c> bricht ab und nennt den Namen.
    /// </remarks>
    [Theory]
    [InlineData("WORKERTRANSFER_JWT_SECRET")]
    [InlineData("WORKERTRANSFER_NOTIFY_SECRET")]
    [InlineData("WORKERTRANSFER_ERASURE_SECRET")]
    public void Compose_haelt_fuer_kein_Geheimnis_eine_Vorgabe_bereit(string name)
    {
        var roh = File.ReadAllText(Path.Combine(Wurzel(), "docker-compose.yml"));
        var ohneKommentar = string.Join(
            "\n", roh.Split('\n').Where(z => !z.TrimStart().StartsWith('#')));

        ohneKommentar.Should().NotContain(
            $"${{{name}:-",
            $"'{name}' haette dann einen Vorgabewert in git");
        ohneKommentar.Should().Contain(
            $"${{{name}:?",
            $"'{name}' muss den Start abbrechen und sich benennen, wenn es fehlt");
    }

    /// <summary>Jeder Einstiegspunkt lädt die Umgebung, und zwar als Erstes.</summary>
    /// <remarks>
    /// „Als Erstes" ist keine Kosmetik: der Konfigurationsaufbau liest die
    /// Umgebungsvariablen beim Bauen des <c>WebApplicationBuilder</c>. Wer
    /// danach lädt, hat geladen und niemand liest es.
    /// </remarks>
    [Fact]
    public void Jeder_Dienst_laedt_die_Umgebung_vor_dem_Bauen()
    {
        var einstiege = Directory
            .EnumerateFiles(Path.Combine(Wurzel(), "src"), "Program.cs", SearchOption.AllDirectories)
            .Where(pfad => !pfad.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !pfad.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        einstiege.Should().HaveCount(14, "dreizehn Dienste und das Gateway");

        foreach (var pfad in einstiege)
        {
            var text = File.ReadAllText(pfad);
            var laden = text.IndexOf("Umgebung.Laden()", StringComparison.Ordinal);
            var bauen = text.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal);

            laden.Should().BeGreaterThan(
                -1, $"'{Path.GetFileName(Path.GetDirectoryName(pfad))}' laedt die Umgebung nicht");
            laden.Should().BeLessThan(
                bauen,
                $"'{Path.GetFileName(Path.GetDirectoryName(pfad))}' laedt sie zu spaet — "
                + "der Konfigurationsaufbau hat die Umgebung dann schon gelesen");
        }
    }

    /// <summary>
    /// <c>Umgebung.Laden()</c> setzt, was fehlt — und überschreibt nicht, was da ist.
    /// </summary>
    /// <remarks>
    /// Die zweite Hälfte ist die wichtigere. In Compose und in Kubernetes kommt
    /// die Umgebung von dort; eine Datei, die im Bild liegen bleibt, dürfte das
    /// nie übersteuern — sonst entschiede der Inhalt des Bildes über die
    /// Geheimnisse der Installation.
    /// </remarks>
    [Fact]
    public void Laden_setzt_Fehlendes_und_laesst_Gesetztes_stehen()
    {
        var ordner = Directory.CreateTempSubdirectory("wt-env-");

        // Eindeutige Namen, damit parallele Reihen sich nicht ins Gehege kommen.
        var kennung = Guid.NewGuid().ToString("N")[..8];
        var neuer = $"WT_PROBE_NEU_{kennung}";
        var alter = $"WT_PROBE_ALT_{kennung}";

        try
        {
            File.WriteAllText(
                Path.Combine(ordner.FullName, ".env"),
                $"{neuer}=aus-der-datei\n{alter}=aus-der-datei\n");

            Environment.SetEnvironmentVariable(alter, "aus-der-umgebung");

            var geladen = Umgebung.Laden(ordner.FullName);

            geladen.Should().EndWith(".env");
            Environment.GetEnvironmentVariable(neuer).Should().Be("aus-der-datei");
            Environment.GetEnvironmentVariable(alter).Should().Be(
                "aus-der-umgebung",
                "eine gesetzte Variable gewinnt — im Container kommt sie von aussen");
        }
        finally
        {
            Environment.SetEnvironmentVariable(neuer, null);
            Environment.SetEnvironmentVariable(alter, null);
            ordner.Delete(recursive: true);
        }
    }

    /// <summary>Ohne Datei passiert nichts, und das ist kein Fehler.</summary>
    /// <remarks>
    /// Im Container gibt es keine <c>.env</c> — die Umgebung kommt von Compose
    /// oder vom Chart. Ein Abbruch hier wäre ein Abbruch im Regelfall.
    /// </remarks>
    [Fact]
    public void Ohne_Datei_passiert_nichts()
    {
        var ordner = Directory.CreateTempSubdirectory("wt-env-leer-");

        try
        {
            Umgebung.Laden(ordner.FullName).Should().BeNull();
        }
        finally
        {
            ordner.Delete(recursive: true);
        }
    }

}
