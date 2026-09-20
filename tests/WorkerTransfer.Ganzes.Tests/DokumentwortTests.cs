using System.Text.RegularExpressions;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Die Worte in den drei Dokumenten — geprüft ohne laufenden Stapel.
/// </summary>
/// <remarks>
/// <para><strong>Warum ein .NET-Test über eine Python-Datei.</strong> Die drei
/// Geltungssätze und der Vorbehalt sind die Sätze, die in einem
/// <em>unterschriebenen</em> Dokument stehen und die jemand herumreicht. Sie
/// entstehen im Erzeuger, und der ist ein Skript — aber geprüft werden müssen
/// sie <em>bevor</em> ein Stapel läuft, denn sonst prüft sie niemand, solange
/// niemand <c>make nachweis</c> aufruft. Eine Quelle, zwei Leser: das Skript
/// benutzt die Konstanten, dieser Test liest sie.</para>
///
/// <para><strong>Und warum nicht nur am erzeugten Dokument.</strong>
/// <c>scripts/nachweis_worte.py</c> prüft, was wirklich herauskam, samt der
/// Vorbehalte, die aus der Lesung abgeleitet werden — das ist die schärfere
/// Hälfte und braucht den Stapel. Diese hier ist die, die immer läuft. Zwei
/// Hälften, weil eine allein jeweils eine Lücke lässt.</para>
///
/// <para><strong>Der Vorbehaltssatz wird andersherum geprüft.</strong> Er
/// <em>muss</em> „zertifiziert“ und „Bescheinigung“ enthalten, weil er sie
/// verneint. Gemessen: die Wortsuche schlug beim ersten Lauf genau auf ihm an.
/// Also wird hier festgehalten, dass die Verneinung dasteht — ein Vorbehalt,
/// aus dem sie jemand herausnimmt, fällt damit auf.</para>
/// </remarks>
public class DokumentwortTests
{
    /// <summary>Worte, die eine Zusage machen, die niemand hier geben darf.</summary>
    private static readonly string[] Untersagt =
    [
        "zertifi", "konform", "compliant", "certif", "bescheinig",
        "attest", "auditiert", "erfuellt art", "erfüllt art"
    ];

    /// <summary>Kein Geltungssatz behauptet Konformität.</summary>
    [Fact]
    public void Kein_Geltungssatz_behauptet_Konformitaet()
    {
        var saetze = Geltungssaetze();

        saetze.Should().HaveCount(3, "drei Dokumente, drei Leser");

        foreach (var (dokument, satz) in saetze)
        {
            foreach (var wort in Untersagt)
            {
                satz.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"der Geltungssatz von „{dokument}“ trägt „{wort}“ — "
                        + "Belege, keine Konformität");
            }
        }
    }

    /// <summary>Jeder Geltungssatz sagt, was er NICHT enthält.</summary>
    /// <remarks>
    /// <strong>Das ist die Hälfte, die ihn ehrlich macht.</strong> Ein Umfang,
    /// der nur sagt, was drin ist, wird gelesen, als wäre alles andere in
    /// Ordnung — und ein Dokument über einen KI-Einsatz, das nicht sagt, dass
    /// es nichts über den Inhalt der Prompts weiß, verspricht genau das.
    /// </remarks>
    [Fact]
    public void Jeder_Geltungssatz_sagt_auch_was_er_nicht_enthaelt()
    {
        foreach (var (dokument, satz) in Geltungssaetze())
        {
            satz.Should().Contain(
                "NICHT enthalten",
                $"der Geltungssatz von „{dokument}“ muss seine eigene Grenze nennen");
        }
    }

    /// <summary>Der Vorbehalt verneint, und die Verneinung steht da.</summary>
    /// <remarks>
    /// Drei Aussagen, und jede fehlt in den Broschüren, gegen die dieses
    /// Dokument antritt: maschinell erzeugt, kein Zertifikat, von keiner
    /// akkreditierten Stelle beurteilt.
    /// </remarks>
    [Fact]
    public void Der_Vorbehalt_sagt_dass_es_kein_Zertifikat_ist()
    {
        var vorbehalt = Vorbehalt();

        vorbehalt.Should().Contain("maschinell erzeugte technische Evidenz");
        vorbehalt.Should().Contain("KEIN Zertifikat");
        vorbehalt.Should().Contain("akkreditierte Stelle");
        vorbehalt.Should().Contain("Art. 42/43 DSGVO");
        vorbehalt.Should().Contain("EN ISO/IEC 17065");

        vorbehalt.Should().Contain(
            "keine solche Stelle hat dieses Dokument",
            "ohne diesen Halbsatz liest sich die Nennung der akkreditierten "
            + "Stelle wie ein Hinweis darauf, dass eine beteiligt war");
    }

    /// <summary>Der Vorbehalt gibt die Angemessenheitsfrage weiter.</summary>
    [Fact]
    public void Der_Vorbehalt_gibt_die_Frage_an_einen_Menschen_weiter()
    {
        Vorbehalt().Should().Contain("entscheidet ein Mensch");

        Vorbehalt().Should().NotContain(
            "nicht hochriskant",
            "die Einstufung gehört nicht uns — das Dokument legt die Belege "
            + "daneben und stuft nichts ein");
    }

    /// <summary>Die Erkennung selbst stimmt.</summary>
    [Theory]
    [InlineData("Eine Tatsache, nach der Art. 30 fragt.", false)]
    [InlineData("NICHT enthalten: eine Einstufung.", false)]
    [InlineData("Dieses System ist DSGVO-konform.", true)]
    [InlineData("Hiermit zertifiziert.", true)]
    public void Die_Wortliste_trifft_genau_das_Gemeinte(string satz, bool erwartet) =>
        Untersagt.Any(wort => satz.Contains(wort, StringComparison.OrdinalIgnoreCase))
            .Should().Be(erwartet);

    // ------------------------------------------------------------- Handwerk

    /// <summary>Die drei Geltungssätze, aus dem Erzeuger gelesen.</summary>
    private static IReadOnlyList<(string Dokument, string Satz)> Geltungssaetze()
    {
        var quelle = Quelle();

        return
        [
            .. Regex.Matches(
                    quelle,
                    "\"(?<name>datenschutz|ki|mitbestimmung)\":\\s*\\{.*?"
                    + "\"scope\":\\s*\\((?<satz>.*?)\\),\\s*\"areas\"",
                    RegexOptions.Singleline,
                    TimeSpan.FromSeconds(5))
                .Select(treffer => (
                    treffer.Groups["name"].Value,
                    Zusammengesetzt(treffer.Groups["satz"].Value)))
        ];
    }

    /// <summary>Der feste Vorbehaltssatz, aus dem Erzeuger gelesen.</summary>
    private static string Vorbehalt()
    {
        var treffer = Regex.Match(
            Quelle(),
            "VORBEHALT = \\((?<satz>.*?)\\)\\n",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(5));

        treffer.Success.Should().BeTrue(
            "ohne den Vorbehalt prüft diese Reihe nichts — und dann ist sie "
            + "grün über einem Dokument, das ihn womöglich gar nicht mehr trägt");

        return Zusammengesetzt(treffer.Groups["satz"].Value);
    }

    /// <summary>Aus den aneinandergereihten Python-Literalen wird ein Satz.</summary>
    private static string Zusammengesetzt(string roh) =>
        string.Concat(
            Regex.Matches(roh, "\"(?<teil>[^\"]*)\"", RegexOptions.None,
                    TimeSpan.FromSeconds(5))
                .Select(teil => teil.Groups["teil"].Value));

    /// <summary>Der Erzeuger der Dokumente.</summary>
    /// <remarks>
    /// Gesucht wird vom Testverzeichnis aufwärts, weil das Arbeitsverzeichnis
    /// einer Testreihe das Ausgabeverzeichnis ist und nicht die Wurzel des
    /// Baumes.
    /// </remarks>
    private static string Quelle()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null)
        {
            var datei = Path.Combine(
                verzeichnis.FullName, "scripts", "nachweis_dokumente.py");

            if (File.Exists(datei))
            {
                return File.ReadAllText(datei);
            }

            verzeichnis = verzeichnis.Parent;
        }

        throw new FileNotFoundException(
            "scripts/nachweis_dokumente.py nicht gefunden — ohne sie prüft "
            + "diese Reihe nichts.");
    }
}
