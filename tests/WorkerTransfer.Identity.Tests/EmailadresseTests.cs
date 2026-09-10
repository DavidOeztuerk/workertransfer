using FluentAssertions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Eine Adresse, die nicht zustellbar ist, darf kein Konto anlegen.</summary>
public class EmailadresseTests
{
    [Theory]
    [InlineData("bus")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.org")]
    [InlineData("anna@")]
    [InlineData("anna example.org")]
    // Syntaktisch erlaubt, im offenen Netz nicht zustellbar.
    [InlineData("anna@localhost")]
    // Gültige Absenderzeile, aber kein Postfach.
    [InlineData("Anna <anna@example.org>")]
    public void Was_nicht_zustellbar_ist_faellt_durch(string adresse) =>
        Emailadresse.IstZustellbar(adresse).Should().BeFalse();

    [Theory]
    [InlineData("anna@example.org")]
    [InlineData("anna.beispiel@firma.co.uk")]
    [InlineData("anna+bewerbung@example.org")]
    [InlineData("a@b.de")]
    [InlineData("jörg@example.org")]
    public void Was_zustellbar_ist_kommt_durch(string adresse) =>
        Emailadresse.IstZustellbar(adresse).Should().BeTrue();

    [Fact]
    public void Eine_masslos_lange_Adresse_faellt_durch()
    {
        var zulang = new string('a', Emailadresse.Hoechstlaenge) + "@example.org";

        Emailadresse.IstZustellbar(zulang).Should().BeFalse();
    }

    /// <summary>Prüfung und Versand sprechen dieselbe Grammatik.</summary>
    /// <remarks>
    /// Was hier durchkommt, muss sich als <c>MailMessage</c> bauen lassen —
    /// sonst steht die Zeile in der Datenbank und die Mail geht nie hinaus.
    /// </remarks>
    [Theory]
    [InlineData("anna@example.org")]
    [InlineData("anna.beispiel@firma.co.uk")]
    [InlineData("a@b.de")]
    public void Was_durchkommt_laesst_sich_auch_versenden(string adresse)
    {
        Emailadresse.IstZustellbar(adresse).Should().BeTrue();

        var bauen = () => new System.Net.Mail.MailMessage("absender@example.org", adresse);

        bauen.Should().NotThrow();
    }
}
