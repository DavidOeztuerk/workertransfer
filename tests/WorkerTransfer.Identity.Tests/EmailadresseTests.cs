using FluentAssertions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Eine Adresse, die nicht zustellbar ist, darf kein Konto anlegen.</summary>
/// <remarks>
/// <strong>Der Anlass ist gemessen, nicht ausgedacht.</strong> Am 10.09.2026
/// legte <c>POST /auth/register</c> ein Konto mit der Adresse <c>bus</c> an und
/// antwortete <c>201</c>. Die Mail scheiterte erst beim Versand — in einem
/// Hintergrundlauf, der den Fehler protokolliert und verschluckt. Zurück blieb
/// ein Konto auf <c>pending</c>, das niemand je bestätigen kann, während die
/// Oberfläche „wir haben dir eine E-Mail geschickt" versprach.
/// </remarks>
public class EmailadresseTests
{
    /// <summary>Der Fall, der es ausgelöst hat, und seine Verwandten.</summary>
    [Theory]
    [InlineData("bus")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@example.org")]
    [InlineData("anna@")]
    [InlineData("anna example.org")]
    // Syntaktisch erlaubt und im offenen Netz nicht zustellbar — dieselbe
    // stumme Sackgasse wie `bus`.
    [InlineData("anna@localhost")]
    // Gültige ABSENDERZEILE, aber kein Postfach: so stünde ein Anzeigename in
    // der Spalte, die den Menschen identifiziert.
    [InlineData("Anna <anna@example.org>")]
    public void Was_nicht_zustellbar_ist_faellt_durch(string adresse) =>
        Emailadresse.IstZustellbar(adresse).Should().BeFalse();

    [Theory]
    [InlineData("anna@example.org")]
    [InlineData("anna.beispiel@firma.co.uk")]
    [InlineData("anna+bewerbung@example.org")]
    [InlineData("a@b.de")]
    // Umlaute im lokalen Teil sind erlaubt; wer sie hat, soll sich anmelden
    // können.
    [InlineData("jörg@example.org")]
    public void Was_zustellbar_ist_kommt_durch(string adresse) =>
        Emailadresse.IstZustellbar(adresse).Should().BeTrue();

    [Fact]
    public void Eine_masslos_lange_Adresse_faellt_durch()
    {
        // Ohne Grenze nimmt die Spalte an, was ein Aufrufer in einer Schleife
        // baut — und der Parser hätte gegen 10.000 formal gültige Zeichen
        // nichts einzuwenden.
        var zulang = new string('a', Emailadresse.Hoechstlaenge) + "@example.org";

        Emailadresse.IstZustellbar(zulang).Should().BeFalse();
    }

    /// <summary>
    /// Die Prüfung und der Versand müssen DIESELBE Grammatik sprechen.
    /// </summary>
    /// <remarks>
    /// Das ist die eigentliche Zusage dieser Datei. Ein eigener regulärer
    /// Ausdruck wäre eine zweite Grammatik neben der von <c>MailAddress</c>,
    /// und der Spalt zwischen beiden ist genau die Lücke, durch die <c>bus</c>
    /// gekommen ist. Deshalb wird hier nachgestellt, was <c>SmtpVersender</c>
    /// tut: was durchkommt, muss sich als Nachricht bauen lassen.
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
