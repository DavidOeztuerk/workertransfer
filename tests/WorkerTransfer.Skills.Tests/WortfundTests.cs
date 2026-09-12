using FluentAssertions;
using WorkerTransfer.Skills;

namespace WorkerTransfer.Skills.Tests;

/// <summary>
/// Der Wortfund sucht, was der Wortschatz kennt — und rät nie.
/// </summary>
/// <remarks>
/// <para>Die Reihe hält beide Hälften fest, und die zweite ist die wichtigere:
/// ein Finder, der auch fündig wird, wo nichts steht, ist schlimmer als keiner.
/// Er böte einem Menschen ein Wort über sich selbst an, das in seinem Zeugnis
/// nie stand — und das Zeugnis liest niemand nach.</para>
///
/// <para>Deshalb steht neben jeder Zusage ihre Gegenprobe: „MIG/MAG" wird
/// gefunden, „Schweissen" daraus nicht gefolgert; „WIG" wird gefunden, das
/// englische „wig" nicht.</para>
/// </remarks>
public class WortfundTests
{
    /// <summary>Die Schreibweise aus dem Zeugnis, der kanonische Name zurück.</summary>
    /// <remarks>
    /// Der Fall, um dessentwillen es diese Funktion gibt: im Zeugnis steht die
    /// Schreibweise ohne Eszett, im Profil soll der Name stehen, den auch eine
    /// Stelle nennt. Genau das ist eine Aussage über <em>Sprache</em> und
    /// erlaubt (ADR-0023).
    /// </remarks>
    [Theory]
    [InlineData("Herr Meier ist geprüfter Schweissfachmann.", "Schweißfachmann")]
    [InlineData("Herr Meier ist geprüfter Schweißfachmann (DVS).", "Schweißfachmann")]
    [InlineData("Beherrscht MIG/MAG-Schweißen sicher.", "MIG/MAG")]
    [InlineData("Kenntnisse in mig-mag.", "MIG/MAG")]
    [InlineData("Ausbildung zur Pflegefachfrau abgeschlossen.", "Pflegefachkraft")]
    [InlineData("Der Gabelstaplerschein liegt vor.", "Staplerschein")]
    [InlineData("Arbeitete mit postgres und node.", "PostgreSQL")]
    public void Eine_bekannte_Schreibweise_wird_zu_ihrem_Namen(string text, string name) =>
        Wortfund.Finde(text).Should().Contain(name);

    /// <summary>
    /// <strong>Aus „MIG/MAG" folgt kein „Schweißen".</strong>
    /// </summary>
    /// <remarks>
    /// Die Gegenprobe zur Verlockung, nicht die Bestätigung der Funktion —
    /// dasselbe Paar wie <c>Aus_React_folgt_kein_JavaScript</c> im Wortschatz,
    /// nur an der Stelle, an der es schwerer zu halten ist: wer einen Text
    /// durchsucht, hat die verwandten Wörter buchstäblich vor sich.
    /// </remarks>
    [Fact]
    public void Aus_einem_Fund_folgt_kein_zweites_Wort() =>
        Wortfund.Finde("Beherrscht MIG/MAG.").Should().Equal("MIG/MAG");

    /// <summary>Ein Wort im Wortinneren ist kein Fund.</summary>
    /// <remarks>
    /// Ohne Wortgrenze fände „SPS" sich in jedem Text mit dieser Buchstabenfolge
    /// und „go" in „gogo". Der Bindestrich dagegen IST eine Grenze: in
    /// „SPS-Programmierung" steht SPS.
    /// </remarks>
    [Theory]
    [InlineData("Die Abkürzung stand mitten in ASPSPS drin.")]
    [InlineData("Er hat rechtzeitig und sorgfältig gearbeitet.")]
    public void Ein_Wortinneres_ist_kein_Fund(string text) =>
        Wortfund.Finde(text).Should().BeEmpty();

    /// <summary>Ein Bindestrich trennt, er verbirgt nicht.</summary>
    [Fact]
    public void Ein_Bindestrich_ist_eine_Wortgrenze() =>
        Wortfund.Finde("Erfahrung in der SPS-Programmierung.").Should().Equal("SPS");

    /// <summary>
    /// <strong>Kurze Kürzel nur in Grossbuchstaben.</strong>
    /// </summary>
    /// <remarks>
    /// „wig" ist im Englischen eine Perücke, „go" ein alltägliches Verb. Ein
    /// falscher Vorschlag ist hier teurer als ein fehlender: er bietet einem
    /// Menschen ein Wort über sich selbst an, das in seinem Dokument nie stand.
    /// </remarks>
    [Theory]
    [InlineData("She was wearing a wig at the ceremony.")]
    [InlineData("Please go to the office and ask.")]
    public void Ein_kurzes_Kuerzel_in_Kleinschreibung_ist_kein_Fund(string text) =>
        Wortfund.Finde(text).Should().BeEmpty();

    /// <summary>Die Gegenhälfte — sonst hiesse der Befund oben „es trifft gar nichts".</summary>
    [Theory]
    [InlineData("Schweissverfahren WIG und Autogen.", "WIG")]
    [InlineData("Programmierung von CNC-Maschinen.", "CNC")]
    public void Dasselbe_Kuerzel_in_Versalien_ist_ein_Fund(string text, string name) =>
        Wortfund.Finde(text).Should().Contain(name);

    /// <summary>Ein Zeichen im Namen hebt die Versalregel auf.</summary>
    /// <remarks>
    /// „c#" und „k8s" sind auch kleingeschrieben unverwechselbar — die Regel
    /// oben schützt gegen Buchstabenfolgen, die zufällig auch Wörter sind, und
    /// diese hier sind keine.
    /// </remarks>
    [Theory]
    [InlineData("Programmiert in c# seit 2019.", "C#")]
    [InlineData("Betrieb auf k8s.", "Kubernetes")]
    public void Ein_Kuerzel_mit_Sonderzeichen_gilt_in_jeder_Schreibung(string text, string name) =>
        Wortfund.Finde(text).Should().Contain(name);

    /// <summary>Der längere Name gewinnt gegen das kürzere Stück in ihm.</summary>
    /// <remarks>
    /// Ohne diese Reihenfolge fände „MySQL" sich in „Microsoft SQL Server"
    /// nicht — aber das kürzere Fenster gewönne, und der längere Name käme nie
    /// zum Zug. Gesucht wird deshalb vom längsten Fenster abwärts.
    /// </remarks>
    [Fact]
    public void Der_laengere_Name_gewinnt() =>
        Wortfund.Finde("Betrieb von Microsoft SQL Server.").Should().Equal("Microsoft SQL Server");

    /// <summary>Ein Zeilenumbruch mitten im Namen trennt ihn nicht.</summary>
    /// <remarks>
    /// In einem gesetzten Dokument steht irgendwann zwischen jedem Wortpaar ein
    /// Umbruch. „Erste Hilfe" fände sich sonst selbst nicht.
    /// </remarks>
    [Fact]
    public void Ein_Umbruch_im_Namen_trennt_ihn_nicht() =>
        Wortfund.Finde("Teilnahme am Kurs\n  Erste\nHilfe   im Betrieb.").Should().Equal("Erste Hilfe");

    /// <summary>Jeder Name einmal, in der Reihenfolge seines ersten Vorkommens.</summary>
    /// <remarks>
    /// Eine Reihenfolge über <em>Wörter</em> in <em>einem</em> Dokument. Eine
    /// Zahl entsteht daraus nirgends, und aus ihr folgt keine Rangfolge über
    /// irgendjemanden (ADR-0022).
    /// </remarks>
    [Fact]
    public void Jeder_Name_einmal_in_der_Reihenfolge_des_Textes() =>
        Wortfund.Finde("CNC, dann SPS, dann nochmal CNC und zuletzt WIG.")
            .Should().Equal("CNC", "SPS", "WIG");

    /// <summary>Was der Wortschatz nicht kennt, wird nicht gefunden.</summary>
    /// <remarks>
    /// <strong>Und das ist die Grenze, nicht die Lücke.</strong> „Bohrwerksdreher"
    /// ist ein echter Beruf; ihn hier zu erfinden hiesse, aus der Gestalt eines
    /// Wortes zu schliessen, es sei eine Fähigkeit. Wer ihn vermisst, erweitert
    /// den Wortschatz per Pull Request — er wird nie aus den Unterlagen von
    /// Menschen gelernt.
    /// </remarks>
    [Fact]
    public void Ein_unbekanntes_Wort_wird_nicht_erfunden() =>
        Wortfund.Finde("Er arbeitete als Bohrwerksdreher und Rohrleitungsbauer.")
            .Should().BeEmpty();

    /// <summary>Kein Text, kein Fund — und kein Fehler.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Ohne_Text_gibt_es_nichts_zu_finden(string? text) =>
        Wortfund.Finde(text).Should().BeEmpty();
}
