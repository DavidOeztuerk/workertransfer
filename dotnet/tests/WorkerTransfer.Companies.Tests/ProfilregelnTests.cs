using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Companies.Domain.Arbeitgeberprofile;

namespace WorkerTransfer.Companies.Tests;

/// <summary>Regeln, die am Aggregat hängen und nicht an einer Route.</summary>
/// <remarks>
/// Diese Reihe gibt es, weil eine Gegenprobe eine Lücke gezeigt hat: die
/// Reihenfolge „erst vollständig prüfen, dann schreiben" ließ sich über HTTP
/// nicht widerlegen. Der Speicher gibt gelesene Zeilen als <em>neues</em>
/// Aggregat zurück und schreibt nur über <c>SichereAsync</c>, das nach einer
/// Ausnahme gar nicht mehr aufgerufen wird — die halb geänderte Instanz wird
/// einfach verworfen. Die Regel schützt also den, der das Aggregat über den
/// Fehlschlag hinaus in der Hand behält, und genau dort muss sie geprüft
/// werden.
/// </remarks>
public class ProfilregelnTests
{
    private static Arbeitgeberprofil Profil() =>
        Arbeitgeberprofil.Lege_an(
            new TenantId(Guid.CreateVersion7()),
            "muster",
            "Erste Fassung",
            "Über uns.",
            "https://muster.example",
            ["Berlin"],
            ["Homeoffice"],
            DateTimeOffset.UnixEpoch);

    /// <summary>
    /// Ein abgelehntes Formular darf kein halb geändertes Aggregat
    /// hinterlassen.
    /// </summary>
    [Fact]
    public void Eine_abgewiesene_Aenderung_laesst_das_Aggregat_unberuehrt()
    {
        var profil = Profil();

        var versuch = () => profil.Aendere(
            "Zweite Fassung", "Ganz anders.", "ftp://muster.example",
            ["Hamburg"], ["Dienstrad"], DateTimeOffset.UnixEpoch.AddDays(1));

        versuch.Should().Throw<Linkfehler>();

        profil.Anzeigename.Should().Be("Erste Fassung");
        profil.UeberUns.Should().Be("Über uns.");
        profil.Netzseite.Should().Be("https://muster.example");
        profil.Orte.Should().Equal("Berlin");
        profil.Leistungen.Should().Equal("Homeoffice");
        profil.GeaendertAm.Should().Be(DateTimeOffset.UnixEpoch);
    }

    /// <summary>Das Kürzel hat keinen Weg, sich zu ändern.</summary>
    /// <remarks>
    /// Nicht „wird nicht geändert", sondern <em>kann</em> nicht: es gibt keine
    /// Zuweisung dafür. Diese Prüfung fällt, sobald jemand eine hinzufügt — und
    /// das ist der Punkt, an dem ein geteilter Link zu brechen beginnt.
    /// </remarks>
    [Fact]
    public void Das_Kuerzel_hat_keinen_Setzer()
    {
        typeof(Arbeitgeberprofil)
            .GetProperty(nameof(Arbeitgeberprofil.Kuerzel))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>Umlaute werden zerlegt, ihre Grundbuchstaben bleiben.</summary>
    [Theory]
    [InlineData("Grün & Söhne GmbH", "grun-sohne-gmbh")]
    [InlineData("  Führende   Technik  ", "fuhrende-technik")]
    [InlineData("ACME", "acme")]
    [InlineData("株式会社", Kuerzel.Rueckfall)]
    [InlineData("", Kuerzel.Rueckfall)]
    [InlineData("---", Kuerzel.Rueckfall)]
    public void Aus_dem_Namen_wird_ein_Kuerzel(string name, string erwartet)
    {
        Kuerzel.Aus(name).Should().Be(erwartet);
    }

    /// <summary>Ein Kürzel endet nie auf einem Trennstrich.</summary>
    /// <remarks>
    /// Der Zähler hängt <c>-2</c> an; ein Kürzel, das schon auf einen
    /// Trennstrich endet, ergäbe <c>marke--2</c>.
    /// </remarks>
    [Fact]
    public void Ein_abgeschnittenes_Kuerzel_endet_nicht_auf_einem_Trennstrich()
    {
        var sehrLang = string.Join(" ", Enumerable.Repeat("wort", 40));

        var kuerzel = Kuerzel.Aus(sehrLang);

        kuerzel.Length.Should().BeLessThanOrEqualTo(Kuerzel.Hoechstlaenge);
        kuerzel.Should().NotEndWith("-");
        kuerzel.Should().NotStartWith("-");
    }
}
