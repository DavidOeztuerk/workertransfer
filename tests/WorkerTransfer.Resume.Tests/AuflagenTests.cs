using System.Reflection;
using FluentAssertions;
using WorkerTransfer.Resume.Contracts;
using WorkerTransfer.Resume.Domain.Lebenslaeufe;
using WorkerTransfer.Resume.Infrastructure.Persistence;

namespace WorkerTransfer.Resume.Tests;

/// <summary>
/// Die Auflagen, unter denen ein Index über personenbezogene Texte hier
/// überhaupt bestehen darf (PBI-7, ADR-0043).
/// </summary>
/// <remarks>
/// <para>Sie stehen in <em>einer</em> Datei, weil sie zusammen die Begründung
/// sind: ein Index über die Zeugnisse von Menschen ist der eine Ort, an dem
/// dieses System so etwas zulässt, und er hängt an drei Sätzen. Wer einen davon
/// rot macht, hat nicht einen Test gebrochen, sondern die Erlaubnis.</para>
///
/// <para>Geprüft wird an <em>Typen</em> statt an Verhalten, wo es geht: eine
/// Verhaltensprobe beantwortet „tut es das heute nicht?", eine Typprobe
/// verlangt, dass wer es ändert, die Menge hier hinschreibt. Dieselbe Bewegung,
/// aber eine, die auffällt.</para>
/// </remarks>
public class AuflagenTests
{
    // ---------------------------------------------------------------------
    // AUFLAGE 1 — ER IST NICHT DURCHSUCHBAR
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>Der Index kennt keine Frage nach dem Wort.</strong>
    /// </summary>
    /// <remarks>
    /// „Welche Menschen tragen ‚Schweißfachmann' in ihren Unterlagen" ist genau
    /// die Suche, die dieses System nicht hat und nicht bekommt: durchsuchbar
    /// ist allein, was jemand selbst in sein Profil getippt und <em>gespeichert</em>
    /// hat (ADR-0033). Gefragt werden darf nur nach den eigenen Unterlagen.
    /// <para>
    /// Ein Mengenvergleich und kein Enthaltensein — eine vierte Methode fällt
    /// dann auf, statt mitzulaufen. Dieselbe Bewegung wie in
    /// <c>TokenformTests</c>, wo ein Test zwei Namen verbot und einen neunten
    /// Anspruch nie bemerkte.
    /// </para>
    /// </remarks>
    [Fact]
    public void Der_Index_kennt_keine_Frage_nach_dem_Wort() =>
        typeof(IFundSpeicher).GetMethods().Select(glied => glied.Name)
            .Should().BeEquivalentTo("AlleAsync", "SichereAsync", "LoescheAsync");

    /// <summary>Und der Speicher dahinter auch nicht.</summary>
    /// <remarks>
    /// Die Schnittstelle oben liesse sich umgehen, indem jemand eine Abfrage
    /// unmittelbar an die Umsetzung hängt — öffentlich, aber am Port vorbei.
    /// </remarks>
    [Fact]
    public void Auch_der_Speicher_kennt_keine_dritte_Frage() =>
        typeof(EfFundSpeicher).GetMethods(BindingFlags.Public | BindingFlags.Instance
                | BindingFlags.DeclaredOnly)
            .Select(glied => glied.Name)
            .Should().BeEquivalentTo("AlleAsync", "SichereAsync", "LoescheAsync");

    // ---------------------------------------------------------------------
    // AUFLAGE 2 — ER HÄLT NAMEN, NIE DEN WORTLAUT
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>Die Zeile hält keinen Text aus dem Zeugnis.</strong>
    /// </summary>
    /// <remarks>
    /// Der Volltext lebt im Arbeitsspeicher des einen Aufrufs, der ihn gelesen
    /// hat. Eine Spalte mit dem Wortlaut wäre eine zweite Kopie der Unterlage —
    /// diesmal in der Datenbank, und damit in jeder Sicherung und in jedem
    /// Abzug, den irgendwer für eine Fehlersuche zieht (ADR-0035).
    /// <para>
    /// Die ausgeschriebene Feldmenge und keine Verbotsliste: wer ein Feld
    /// hinzufügt, schreibt es hier hin und beantwortet dabei die Frage, ob es
    /// den Menschen oder das Dokument beschreibt.
    /// </para>
    /// </remarks>
    [Fact]
    public void Die_Indexzeile_haelt_genau_diese_Felder() =>
        typeof(FundZeile).GetProperties().Select(feld => feld.Name)
            .Should().BeEquivalentTo(
                "Id", "SubjectId", "DocumentId", "HasText", "Terms", "ReadAt");

    /// <summary>Und hinaus geht dieselbe Menge, um ein Feld ergänzt.</summary>
    /// <remarks>
    /// Der Name der Unterlage reist mit, weil die Oberfläche sagen können muss,
    /// aus <em>welcher</em> Datei nichts zu lesen war. „Nichts gefunden" ohne
    /// die Datei dazu wäre die stillschweigende Behauptung, es stehe nichts
    /// darin (ADR-0022 §3).
    /// </remarks>
    [Fact]
    public void Der_Vertrag_traegt_genau_diese_Felder() =>
        typeof(UnterlagenfundV1).GetProperties().Select(feld => feld.Name)
            .Should().BeEquivalentTo("DocumentId", "Name", "ReadAt", "HasText", "Terms");

    // ---------------------------------------------------------------------
    // AUFLAGE 3 — KEINE ZAHL ÜBER EINEN MENSCHEN
    // ---------------------------------------------------------------------

    /// <summary>
    /// Aus einem Fund wird nichts gerechnet.
    /// </summary>
    /// <remarks>
    /// Die naheliegende Versuchung heisst hier „wie gut passt dieses Zeugnis" —
    /// drei gefundene Wörter von fünf gesuchten, und schon steht die Zahl da,
    /// die ADR-0022 gelöscht hat. Ein Zwilling von <c>Adr0022Tests</c>, über die
    /// Typen dieser Arbeit.
    /// </remarks>
    [Theory]
    [InlineData("score")]
    [InlineData("rank")]
    [InlineData("weight")]
    [InlineData("percent")]
    [InlineData("passung")]
    [InlineData("gewicht")]
    [InlineData("niveau")]
    [InlineData("anzahl")]
    public void Kein_Feld_rechnet_ueber_einen_Menschen(string wort)
    {
        string[] namen =
        [
            .. Felder(typeof(Unterlagenfund)),
            .. Felder(typeof(FundZeile)),
            .. Felder(typeof(UnterlagenfundV1))
        ];

        namen.Should().NotContain(
            name => name.Contains(wort, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------------
    // AUFLAGE 4 — DER ERKENNER RUFT NIEMANDEN AN
    // ---------------------------------------------------------------------

    /// <summary>
    /// <strong>Nach draussen gehen genau zwei Türen — und der Texterkenner ist
    /// keine davon.</strong>
    /// </summary>
    /// <remarks>
    /// <para>Der Ledger und notification-service; beide stehen in
    /// <c>docker-compose.yml</c>, weil die Egress-Grenze ihre erlaubten Hosts
    /// aus der Konfiguration ableitet.</para>
    ///
    /// <para>Ein Texterkenner im Netz wäre die dritte — und es ist die Tür, die
    /// am teuersten falsch ist: durch sie ginge ein Zeugnis mit Arbeitgeber,
    /// Dauer und Note, damit ein Fremder Wörter darin sucht. Ein dritter Typ
    /// mit einer <c>IHttpClientFactory</c> ist der erste Verdacht, und dieser
    /// Test zwingt, ihn hinzuschreiben. Stünde sein Ziel dann nicht in der
    /// Konfiguration, wiese die Grenze ihn ab, ohne eine Zeile zu
    /// protokollieren, und der Knopf täte einfach nichts — daran ist am
    /// 10.09.2026 der Anschreiben-Agent gestorben.</para>
    /// </remarks>
    [Fact]
    public void Nach_draussen_gehen_genau_zwei_Tueren() =>
        typeof(ResumeDbContext).Assembly.GetExportedTypes()
            .Where(typ => typ.GetConstructors().Any(bau => bau.GetParameters()
                .Any(glied => glied.ParameterType == typeof(IHttpClientFactory))))
            .Select(typ => typ.Name)
            .Should().BeEquivalentTo("HttpEinwilligungstor", "HttpBenachrichtigung");

    private static IEnumerable<string> Felder(Type typ) =>
        typ.GetProperties().Select(feld => feld.Name);
}
