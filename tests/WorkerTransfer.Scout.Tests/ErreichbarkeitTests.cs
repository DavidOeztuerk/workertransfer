using FluentAssertions;
using WorkerTransfer.Scout.Domain.Treffer;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Tests;

/// <summary>
/// Entfernung ist eine Frage zwischen zwei Aussagen (ADR-0041).
/// </summary>
/// <remarks>
/// <para>Was hier geprüft wird, ist nicht die Rechnung — Haversine steht seit
/// ADR-0032 und ist dort gemessen. Geprüft wird, <strong>was aus ihr wird</strong>:
/// drei Zustände, keine Zahl, und vier verschiedene Wege zum Strich, von denen
/// keiner ein Kreuz werden darf.</para>
///
/// <para>Die Orte sind echt, und die Entfernungen sind an der mitgelieferten
/// Ortstabelle GEMESSEN, nicht geschätzt: Berlin–Potsdam 27 km,
/// Berlin–Wittenberg 90 km, Berlin–München 505 km. Der erste Entwurf dieser
/// Reihe nahm Berlin–Leipzig für „zwischen 50 und 100" — es sind 150, und der
/// Test fiel zu Recht.</para>
/// </remarks>
public class ErreichbarkeitTests
{
    private static readonly Ortspunkt? Berlin = Ortskunde.Finde("Berlin");
    private static readonly Ortspunkt? Potsdam = Ortskunde.Finde("Potsdam");
    private static readonly Ortspunkt? Wittenberg = Ortskunde.Finde("Wittenberg");
    private static readonly Ortspunkt? Muenchen = Ortskunde.Finde("München");

    /// <summary>Die Ortstabelle kennt diese vier — sonst prüft nichts hier etwas.</summary>
    [Fact]
    public void Die_Ortstabelle_kennt_die_Orte_dieser_Reihe()
    {
        Berlin.Should().NotBeNull();
        Potsdam.Should().NotBeNull();
        Wittenberg.Should().NotBeNull();
        Muenchen.Should().NotBeNull();

        // Und die Entfernungen sind die, mit denen die Faelle unten rechnen.
        // Ohne diese drei Zeilen waere jeder Fall unten eine Annahme ueber die
        // Ortstabelle — und genau daran ist der erste Entwurf gescheitert.
        Ortskunde.EntfernungKm(Berlin!.Value, Potsdam!.Value).Should().BeInRange(20, 35);
        Ortskunde.EntfernungKm(Berlin.Value, Wittenberg!.Value).Should().BeInRange(70, 99);
        Ortskunde.EntfernungKm(Berlin.Value, Muenchen!.Value).Should().BeInRange(450, 550);
    }

    /// <summary>Innerhalb der genannten Stufe: ein Ja.</summary>
    [Fact]
    public void Wer_weit_genug_pendelt_erreicht_die_Stelle() =>
        Erreichbarkeit.Bilde(Pendelstufe.Bis50, Anwesenheit.VorOrt, Berlin, Potsdam)
            .Stand.Should().Be(Erreichbarkeitsstand.Erreichbar);

    /// <summary>Darüber: ein Nein — und der Treffer bleibt trotzdem in der Liste.</summary>
    /// <remarks>
    /// Dass er bleibt, prüft <c>ScoutreiseTests</c> am Endpunkt. Hier steht nur,
    /// dass das Häkchen ehrlich ist.
    /// </remarks>
    [Fact]
    public void Wer_naeher_bleiben_will_bekommt_ein_Kreuz() =>
        Erreichbarkeit.Bilde(Pendelstufe.Bis10, Anwesenheit.VorOrt, Berlin, Muenchen)
            .Stand.Should().Be(Erreichbarkeitsstand.Weiter);

    /// <summary>Die Stufe entscheidet, nicht die Entfernung allein.</summary>
    /// <remarks>
    /// Dieselbe Strecke, zwei Aussagen, zwei Antworten — das ist der ganze
    /// Gedanke des ADR. Berlin–Wittenberg sind 90 km: über 50, unter 100.
    /// </remarks>
    [Theory]
    [InlineData(Pendelstufe.Bis25, Erreichbarkeitsstand.Weiter)]
    [InlineData(Pendelstufe.Bis50, Erreichbarkeitsstand.Weiter)]
    [InlineData(Pendelstufe.Bis100, Erreichbarkeitsstand.Erreichbar)]
    [InlineData(Pendelstufe.Egal, Erreichbarkeitsstand.Erreichbar)]
    public void Dieselbe_Strecke_vier_Aussagen_zwei_Antworten(
        Pendelstufe stufe, Erreichbarkeitsstand erwartet) =>
        Erreichbarkeit.Bilde(stufe, Anwesenheit.VorOrt, Berlin, Wittenberg)
            .Stand.Should().Be(erwartet);

    /// <summary>„Egal" ist ein Ja, ohne dass irgendetwas gerechnet wird.</summary>
    [Fact]
    public void Egal_erreicht_alles() =>
        Erreichbarkeit.Bilde(Pendelstufe.Egal, Anwesenheit.VorOrt, Berlin, Muenchen)
            .Stand.Should().Be(Erreichbarkeitsstand.Erreichbar);

    /// <summary>Remote ist ein Ja, auch über fünfhundert Kilometer.</summary>
    /// <remarks>
    /// Wer nicht kommen muss, erreicht die Stelle. Das ist der einzige Ausstieg,
    /// der ohne Ortskenntnis ein Ja geben darf — und er darf es, weil die Frage
    /// dann gar nicht gestellt ist.
    /// </remarks>
    [Fact]
    public void Remote_erreicht_alles() =>
        Erreichbarkeit.Bilde(Pendelstufe.Bis10, Anwesenheit.Remote, Berlin, Muenchen)
            .Stand.Should().Be(Erreichbarkeitsstand.Erreichbar);

    /// <summary>Hybrid zählt wie vor Ort.</summary>
    /// <remarks>
    /// Eine Entscheidung, keine Ableitung: bei 3+2 fährt jemand drei Tage die
    /// Woche. Die Strecke zu halbieren, weil „nur zwei Tage Homeoffice", wäre
    /// eine Rechnung über einen Menschen, die niemand begründet hat.
    /// </remarks>
    [Fact]
    public void Hybrid_zaehlt_wie_vor_Ort() =>
        Erreichbarkeit.Bilde(Pendelstufe.Bis10, Anwesenheit.Hybrid, Berlin, Muenchen)
            .Stand.Should().Be(Erreichbarkeitsstand.Weiter);

    // ---------------------------------------------------------------------
    // DIE VIER WEGE ZUM STRICH — und keiner darf ein Kreuz werden.
    // ---------------------------------------------------------------------

    /// <summary>Wer nichts gesagt hat, bekommt kein Nein.</summary>
    /// <remarks>
    /// Die Kernauflage: „nichts gesagt" ist nicht „passt nicht" (ADR-0022 §3).
    /// Ein Kreuz hier wäre eine Aussage über einen Menschen, die er nie
    /// getroffen hat.
    /// </remarks>
    [Fact]
    public void Ohne_Angabe_der_Person_gibt_es_einen_Strich()
    {
        var haken = Erreichbarkeit.Bilde(null, Anwesenheit.VorOrt, Berlin, Muenchen);

        haken.Stand.Should().Be(Erreichbarkeitsstand.Ungesagt);
        haken.Stand.Should().NotBe(Erreichbarkeitsstand.Weiter);
    }

    /// <summary>Ohne Stelle gibt es nichts zu vergleichen.</summary>
    [Fact]
    public void Ohne_Stelle_gibt_es_einen_Strich() =>
        Erreichbarkeit.Bilde(Pendelstufe.Bis10, null, Berlin, Muenchen)
            .Stand.Should().Be(Erreichbarkeitsstand.Ungesagt);

    /// <summary>Ein unbekannter Ort ist ein Loch bei UNS, kein Nein über die Person.</summary>
    /// <remarks>
    /// Die Ortstabelle kennt DE, AT und CH. Wer anderswo wohnt oder seinen Ort
    /// anders schreibt, als sie ihn führt, darf deswegen kein Kreuz bekommen —
    /// sonst wäre unsere Tabelle eine Aussage über einen Menschen.
    /// </remarks>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Ein_unbekannter_Ort_gibt_einen_Strich(bool personBekannt, bool stelleBekannt) =>
        Erreichbarkeit.Bilde(
                Pendelstufe.Bis10,
                Anwesenheit.VorOrt,
                personBekannt ? Berlin : null,
                stelleBekannt ? Muenchen : null)
            .Stand.Should().Be(Erreichbarkeitsstand.Ungesagt);

    /// <summary>Und der Strich trägt trotzdem, was gesagt wurde.</summary>
    /// <remarks>
    /// Ein Strich ist keine Leerstelle: wenn die Person „bis 10" gesagt hat und
    /// nur der Ort fehlt, soll die Karte das zeigen können. Sonst sähe „wir
    /// wissen es nicht" aus wie „sie hat nichts gesagt".
    /// </remarks>
    [Fact]
    public void Der_Strich_traegt_die_Aussagen_die_es_gibt()
    {
        var haken = Erreichbarkeit.Bilde(
            Pendelstufe.Bis10, Anwesenheit.VorOrt, null, Muenchen);

        haken.Stufe.Should().Be(Pendelstufe.Bis10);
        haken.Anwesenheit.Should().Be(Anwesenheit.VorOrt);
    }

    // ---------------------------------------------------------------------

    /// <summary>Die Worte gehen in beide Richtungen durch, unverändert.</summary>
    /// <remarks>
    /// Zwei Schreibweisen für dieselbe Stufe wären zwei Gelegenheiten, eine
    /// Aussage stillschweigend fallen zu lassen — und sie fiele als Strich aus,
    /// nicht als Fehler.
    /// </remarks>
    [Theory]
    [InlineData(Pendelstufe.Bis10)]
    [InlineData(Pendelstufe.Bis25)]
    [InlineData(Pendelstufe.Bis50)]
    [InlineData(Pendelstufe.Bis100)]
    [InlineData(Pendelstufe.Egal)]
    public void Jede_Stufe_ueberlebt_den_Draht(Pendelstufe stufe) =>
        Erreichbarkeitsworte.Stufe(Erreichbarkeitsworte.Wort(stufe)).Should().Be(stufe);

    /// <summary>
    /// Und der Remotegrad von jobs-service wird gelesen, wie er dort heisst.
    /// </summary>
    /// <remarks>
    /// <c>none</c>, <c>hybrid</c>, <c>full</c> — nicht umbenannt. Ein zweiter
    /// Wortschatz für dieselbe Sache ginge beim ersten neuen Wert auseinander.
    /// </remarks>
    [Theory]
    [InlineData("none", Anwesenheit.VorOrt)]
    [InlineData("hybrid", Anwesenheit.Hybrid)]
    [InlineData("full", Anwesenheit.Remote)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Der_Remotegrad_wird_gelesen_wie_er_heisst(string? wort, Anwesenheit? erwartet) =>
        Erreichbarkeitsworte.Anwesenheit(wort).Should().Be(erwartet);

    /// <summary>Ein unbekanntes Wort wird zum Strich, nicht zu einem Fehler.</summary>
    /// <remarks>
    /// Es ist eine freiwillige Angabe. Ein Fehlschlag hier hielte eine ganze
    /// Trefferseite auf, weil ein Wert falsch geschrieben war, den niemand
    /// ausfüllen musste.
    /// </remarks>
    [Fact]
    public void Ein_unbekanntes_Wort_wird_zum_Strich() =>
        Erreichbarkeitsworte.Stufe("bis_42").Should().BeNull();
}
