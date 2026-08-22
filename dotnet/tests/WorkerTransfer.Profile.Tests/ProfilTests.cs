using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Profile.Domain.Faehigkeiten;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Tests;

/// <summary>Das Aggregat: schmal, ohne Sichtbarkeit, ganz oder gar nicht.</summary>
public class ProfilTests
{
    private static readonly DateTimeOffset Jetzt = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private static Profil Beispiel(
        string ueberschrift = "Entwicklerin",
        string text = "Ich baue Dienste.",
        string ort = "Hamburg",
        bool remote = true,
        params string[] faehigkeiten) =>
        Profil.Lege_an(
            SubjectId.New(), ueberschrift, text, ort, remote,
            Faehigkeitenliste.Aus(faehigkeiten), Jetzt);

    /// <summary>
    /// ADR-0020: ob ein Profil gezeigt werden darf, steht ausschließlich im
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
        var namen = typeof(Profil).GetProperties().Select(eigenschaft => eigenschaft.Name);

        namen.Should().NotContain(
            name => name.Contains(verboten, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Die_Person_ist_der_Schluessel()
    {
        var wer = SubjectId.New();

        Profil.Lege_an(wer, "Titel", "", "", false, Faehigkeitenliste.Leer, Jetzt)
            .Wer.Should().Be(wer);
    }

    [Fact]
    public void Weissraum_faellt_weg()
    {
        var profil = Beispiel(ueberschrift: "  Entwicklerin  ", text: "  Hallo  ", ort: "  Kiel ");

        profil.Ueberschrift.Should().Be("Entwicklerin");
        profil.Text.Should().Be("Hallo");
        profil.Ort.Should().Be("Kiel");
    }

    [Fact]
    public void Ohne_Ueberschrift_entsteht_kein_Profil()
    {
        var handlung = () => Beispiel(ueberschrift: "   ");

        handlung.Should().Throw<UeberschriftFehler>();
    }

    [Fact]
    public void Eine_zu_lange_Ueberschrift_wird_abgelehnt()
    {
        var handlung = () =>
            Beispiel(ueberschrift: new string('x', Profil.HoechstlaengeUeberschrift + 1));

        handlung.Should().Throw<UeberschriftFehler>();
    }

    [Fact]
    public void Ein_zu_langer_Text_wird_abgelehnt()
    {
        var handlung = () => Beispiel(text: new string('x', Profil.HoechstlaengeText + 1));

        handlung.Should().Throw<TextFehler>();
    }

    [Fact]
    public void Ein_zu_langer_Ort_wird_abgelehnt()
    {
        var handlung = () => Beispiel(ort: new string('x', Profil.HoechstlaengeOrt + 1));

        handlung.Should().Throw<OrtFehler>();
    }

    /// <summary>
    /// Ein abgelehntes Formular darf kein halb geändertes Aggregat hinterlassen:
    /// wer den Ort ändert und dabei eine leere Überschrift schickt, behält
    /// beides wie es war.
    /// </summary>
    [Fact]
    public void Eine_abgelehnte_Aenderung_aendert_gar_nichts()
    {
        var profil = Beispiel();

        var handlung = () => profil.Aendere(
            "", "Neuer Text", "Berlin", false, Faehigkeitenliste.Leer, Jetzt.AddHours(1));

        handlung.Should().Throw<UeberschriftFehler>();
        profil.Text.Should().Be("Ich baue Dienste.");
        profil.Ort.Should().Be("Hamburg");
        profil.RemoteMoeglich.Should().BeTrue();
        profil.GeaendertAm.Should().Be(Jetzt);
    }

    [Fact]
    public void Eine_Aenderung_setzt_den_Zeitstempel_und_laesst_das_Anlegen_stehen()
    {
        var profil = Beispiel();
        var spaeter = Jetzt.AddDays(3);

        profil.Aendere("Neu", "Text", "Kiel", false, Faehigkeitenliste.Aus(["Go"]), spaeter);

        profil.GeaendertAm.Should().Be(spaeter);
        profil.AngelegtAm.Should().Be(Jetzt);
        profil.Faehigkeiten.Werte.Should().Equal("Go");
    }

    /// <summary>
    /// Eine gespeicherte Zeile beim Lesen abzulehnen hieße, jemandem sein
    /// Profil zu entziehen, weil sich eine Obergrenze geändert hat.
    /// </summary>
    [Fact]
    public void Herstellen_prueft_nicht_erneut()
    {
        var profil = Profil.Stelle_her(
            SubjectId.New(), new string('x', Profil.HoechstlaengeUeberschrift + 40),
            "", "", false, Faehigkeitenliste.Leer, Jetzt, Jetzt);

        profil.Ueberschrift.Should().HaveLength(Profil.HoechstlaengeUeberschrift + 40);
    }
}
