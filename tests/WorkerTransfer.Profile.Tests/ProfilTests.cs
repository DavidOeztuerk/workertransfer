using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Das Aggregat: schmal, ohne Sichtbarkeit, ganz oder gar nicht.</summary>
public class ProfilTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static PersonProfile Beispiel(
        string ueberschrift = "Entwicklerin",
        string text = "Ich baue Dienste.",
        string ort = "Hamburg",
        bool remote = true,
        params string[] skills) =>
        PersonProfile.Lege_an(
            SubjectId.New(), ueberschrift, text, ort, remote,
            SkillList.Aus(skills), Jetzt);

    /// <summary>
    /// ADR-0020: ob ein PersonProfile gezeigt werden darf, steht ausschließlich im
    /// Consent-Ledger. Ein Feld hier wäre eine zweite Wahrheit — und die eine,
    /// die man vergisst mitzuändern.
    /// </summary>
    [Theory]
    [InlineData("sichtbar")]
    [InlineData("visib")]
    [InlineData("public")]
    [InlineData("oeffentlich")]
    [InlineData("freigabe")]
    public void Das_Profil_hat_kein_Sichtbarkeitsfeld(string verboten)
    {
        var namen = typeof(PersonProfile).GetProperties().Select(eigenschaft => eigenschaft.Name);

        namen.Should().NotContain(
            name => name.Contains(verboten, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Die_Person_ist_der_Schluessel()
    {
        var subject = SubjectId.New();

        PersonProfile.Lege_an(subject, "Titel", "", "", false, SkillList.Leer, Jetzt)
            .Wer.Should().Be(subject);
    }

    [Fact]
    public void Weissraum_faellt_weg()
    {
        var profile = Beispiel(ueberschrift: "  Entwicklerin  ", text: "  Hallo  ", ort: "  Kiel ");

        profile.Ueberschrift.Should().Be("Entwicklerin");
        profile.Text.Should().Be("Hallo");
        profile.Ort.Should().Be("Kiel");
    }

    [Fact]
    public void Ohne_Ueberschrift_entsteht_kein_Profil()
    {
        var action = () => Beispiel(ueberschrift: "   ");

        action.Should().Throw<UeberschriftFehler>();
    }

    [Fact]
    public void Eine_zu_lange_Ueberschrift_wird_abgelehnt()
    {
        var action = () =>
            Beispiel(ueberschrift: new string('x', PersonProfile.HoechstlaengeUeberschrift + 1));

        action.Should().Throw<UeberschriftFehler>();
    }

    [Fact]
    public void Ein_zu_langer_Text_wird_abgelehnt()
    {
        var action = () => Beispiel(text: new string('x', PersonProfile.HoechstlaengeText + 1));

        action.Should().Throw<TextFehler>();
    }

    [Fact]
    public void Ein_zu_langer_Ort_wird_abgelehnt()
    {
        var action = () => Beispiel(ort: new string('x', PersonProfile.HoechstlaengeOrt + 1));

        action.Should().Throw<OrtFehler>();
    }

    /// <summary>
    /// Ein abgelehntes Formular darf kein halb geändertes Aggregat hinterlassen:
    /// wer den Ort ändert und dabei eine leere Überschrift schickt, behält
    /// beides wie es war.
    /// </summary>
    [Fact]
    public void Eine_abgelehnte_Aenderung_aendert_gar_nichts()
    {
        var profile = Beispiel();

        var action = () => profile.Aendere(
            "", "Neuer Text", "Berlin", false, SkillList.Leer, Jetzt.AddHours(1));

        action.Should().Throw<UeberschriftFehler>();
        profile.Text.Should().Be("Ich baue Dienste.");
        profile.Ort.Should().Be("Hamburg");
        profile.RemoteMoeglich.Should().BeTrue();
        profile.GeaendertAm.Should().Be(Jetzt);
    }

    [Fact]
    public void Eine_Aenderung_setzt_den_Zeitstempel_und_laesst_das_Anlegen_stehen()
    {
        var profile = Beispiel();
        var spaeter = Jetzt.AddDays(3);

        profile.Aendere("Neu", "Text", "Kiel", false, SkillList.Aus(["Go"]), spaeter);

        profile.GeaendertAm.Should().Be(spaeter);
        profile.AngelegtAm.Should().Be(Jetzt);
        profile.Faehigkeiten.Werte.Should().Equal("Go");
    }

    /// <summary>
    /// Eine gespeicherte Zeile beim Lesen abzulehnen hieße, jemandem sein
    /// PersonProfile zu entziehen, weil sich eine Obergrenze geändert hat.
    /// </summary>
    [Fact]
    public void Herstellen_prueft_nicht_erneut()
    {
        var profile = PersonProfile.Stelle_her(
            SubjectId.New(), new string('x', PersonProfile.HoechstlaengeUeberschrift + 40),
            "", "", false, SkillList.Leer, Jetzt, Jetzt);

        profile.Ueberschrift.Should().HaveLength(PersonProfile.HoechstlaengeUeberschrift + 40);
    }
}
