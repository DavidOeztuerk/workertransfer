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
        EmployerProfile.Create(
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
    public void A_refused_change_leaves_the_aggregate_untouched()
    {
        var profile = Profile();

        var attempt = () => profile.Change(
            "Zweite Fassung", "Ganz anders.", "ftp://muster.example",
            ["Hamburg"], ["Dienstrad"], DateTimeOffset.UnixEpoch.AddDays(1));

        attempt.Should().Throw<LinkError>();

        profile.DisplayName.Should().Be("Erste Fassung");
        profile.About.Should().Be("Über uns.");
        profile.Website.Should().Be("https://muster.example");
        profile.Locations.Should().Equal("Berlin");
        profile.Benefits.Should().Equal("Homeoffice");
        profile.ChangedAt.Should().Be(DateTimeOffset.UnixEpoch);
    }

    /// <summary>Das Kürzel hat keinen Weg, sich zu ändern.</summary>
    /// <remarks>
    /// Nicht „wird nicht geändert", sondern <em>kann</em> nicht: es gibt keine
    /// Zuweisung dafür. Diese Prüfung fällt, sobald jemand eine hinzufügt — und
    /// das ist der Punkt, an dem ein geteilter Link zu brechen beginnt.
    /// </remarks>
    [Fact]
    public void The_slug_has_no_setter()
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
    public void A_name_becomes_a_slug(string name, string erwartet)
    {
        Slug.Aus(name).Should().Be(erwartet);
    }

    /// <summary>Ein Kürzel endet nie auf einem Trennstrich.</summary>
    /// <remarks>
    /// Der Zähler hängt <c>-2</c> an; ein Kürzel, das schon auf einen
    /// Trennstrich endet, ergäbe <c>marke--2</c>.
    /// </remarks>
    [Fact]
    public void A_truncated_slug_does_not_end_on_a_hyphen()
    {
        var sehrLang = string.Join(" ", Enumerable.Repeat("wort", 40));

        var slug = Slug.Aus(sehrLang);

        slug.Length.Should().BeLessThanOrEqualTo(Slug.Hoechstlaenge);
        slug.Should().NotEndWith("-");
        slug.Should().NotStartWith("-");
    }
}
