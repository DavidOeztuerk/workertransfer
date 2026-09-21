using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Companies.Domain.EmployerProfiles;

namespace WorkerTransfer.Companies.Tests;

/// <summary>Regeln, die am Aggregat hängen und nicht an einer Route.</summary>
/// <remarks>
/// Diese Reihe gibt es, weil eine Gegenprobe eine Lücke gezeigt hat: die
/// Reihenfolge „erst vollständig prüfen, dann schreiben" ließ sich über HTTP
/// nicht widerlegen. Der Speicher gibt gelesene Zeilen als <em>neues</em>
/// Aggregat zurück und schreibt nur über <c>SaveAsync</c>, das nach einer
/// Ausnahme gar nicht mehr aufgerufen wird — die halb geänderte Instanz wird
/// einfach verworfen. Die Regel schützt also den, der das Aggregat über den
/// Fehlschlag hinaus in der Hand behält, und genau dort muss sie geprüft
/// werden.
/// </remarks>
public class ProfilregelnTests
{
    private static EmployerProfile Profile() =>
        EmployerProfile.Lege_an(
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
        var profile = Profile();

        var attempt = () => profile.Aendere(
            "Zweite Fassung", "Ganz anders.", "ftp://muster.example",
            ["Hamburg"], ["Dienstrad"], DateTimeOffset.UnixEpoch.AddDays(1));

        attempt.Should().Throw<Linkfehler>();

        profile.Anzeigename.Should().Be("Erste Fassung");
        profile.UeberUns.Should().Be("Über uns.");
        profile.Netzseite.Should().Be("https://muster.example");
        profile.Orte.Should().Equal("Berlin");
        profile.Leistungen.Should().Equal("Homeoffice");
        profile.GeaendertAm.Should().Be(DateTimeOffset.UnixEpoch);
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
        typeof(EmployerProfile)
            .GetProperty(nameof(EmployerProfile.Slug))!
            .CanWrite.Should().BeFalse();
    }

    /// <summary>Umlaute werden zerlegt, ihre Grundbuchstaben bleiben.</summary>
    [Theory]
    [InlineData("Grün & Söhne GmbH", "grun-sohne-gmbh")]
    [InlineData("  Führende   Technik  ", "fuhrende-technik")]
    [InlineData("ACME", "acme")]
    [InlineData("株式会社", Slug.Rueckfall)]
    [InlineData("", Slug.Rueckfall)]
    [InlineData("---", Slug.Rueckfall)]
    public void Aus_dem_Namen_wird_ein_Kuerzel(string name, string erwartet)
    {
        Slug.Aus(name).Should().Be(erwartet);
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

        var slug = Slug.Aus(sehrLang);

        slug.Length.Should().BeLessThanOrEqualTo(Slug.Hoechstlaenge);
        slug.Should().NotEndWith("-");
        slug.Should().NotStartWith("-");
    }
}
