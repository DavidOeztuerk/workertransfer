using FluentAssertions;
using Girder.Core.Identity;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Tests;

/// <summary>The rules of the log, without a database.</summary>
public class LedgerregelnTests
{
    private static readonly SubjectId Anna = new(Guid.CreateVersion7());
    private static readonly Capability Sichtbarkeit = Capability.Parse("profile.visibility:public");
    private static readonly DateTimeOffset Jetzt = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("profile.visibility:public")]
    [InlineData("portfolio.visibility:public")]
    [InlineData("resume.visibility:tenant:9f8e7d6c-5b4a-4938-8271-605f4e3d2c1b")]
    [InlineData("profile.visibility")]
    public void Eine_Faehigkeit_liest_sich(string wert) =>
        Capability.TryParse(wert, out _).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Profile.Visibility:public")]
    [InlineData("p")]
    [InlineData(" profile.visibility:public")]
    [InlineData("profile visibility")]
    public void Und_was_keine_ist_liest_sich_nicht(string wert) =>
        Capability.TryParse(wert, out _).Should().BeFalse();

    [Fact]
    public void Eine_zu_lange_Faehigkeit_wird_abgewiesen() =>
        Capability.TryParse("a." + new string('b', Capability.HoechsteLaenge), out _)
            .Should().BeFalse();

    /// <summary>
    /// The vocabulary is not checked, only the shape.
    /// </summary>
    /// <remarks>
    /// A list of permitted capabilities would be a claim about which
    /// permissions can exist, and consuming services have to be able to ask
    /// about anything — absence is a state, not an error.
    /// </remarks>
    [Fact]
    public void Eine_unbekannte_aber_wohlgeformte_Faehigkeit_wird_angenommen() =>
        Capability.TryParse("gibtesnochnicht.visibility:public", out _).Should().BeTrue();

    [Fact]
    public void Ein_Grund_darf_nicht_leer_sein()
    {
        WithdrawalReason.TryParse("   ", out _).Should().BeFalse();
        WithdrawalReason.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Ein_Grund_darf_nicht_laenger_als_fuenfhundert_Zeichen_sein()
    {
        WithdrawalReason.TryParse(new string('x', 500), out _).Should().BeTrue();
        WithdrawalReason.TryParse(new string('x', 501), out _).Should().BeFalse();
    }

    /// <summary>Withdrawing must always be explainable; granting need not be.</summary>
    [Fact]
    public void Ein_Widerruf_ohne_Grund_kommt_nicht_zustande()
    {
        var ohneGrund = () => ConsentEvent.Restore(
            Guid.CreateVersion7(), Anna, Sichtbarkeit, ConsentAction.Revoke, Jetzt, Anna, null,
            null);

        // Restoring one is fine — see the next test. Recording one is not, and
        // the signature of Revoke() is what says so.
        ohneGrund.Should().NotThrow();
        typeof(ConsentEvent).GetMethod(nameof(ConsentEvent.Revoke))!
            .GetParameters().Should().Contain(p => p.Name == "reason" && !p.IsOptional);
    }

    /// <summary>
    /// After an erasure a withdrawal legitimately has no reason left.
    /// </summary>
    /// <remarks>
    /// ADR-0027 §5 clears the free text of every row of that person, a
    /// withdrawal included. Refusing to read such a row back would make the
    /// erasure the thing that breaks the ledger — and the ledger is what proves
    /// the erasure happened.
    /// </remarks>
    [Fact]
    public void Ein_gelesener_Widerruf_ohne_Grund_ist_kein_Fehler()
    {
        var wiederhergestellt = ConsentEvent.Restore(
            Guid.CreateVersion7(), Anna, Sichtbarkeit, ConsentAction.Revoke, Jetzt, Anna, null,
            null);

        wiederhergestellt.Reason.Should().BeNull();
        Projektion.Stand([wiederhergestellt]).Granted.Should().BeFalse();
    }

    [Fact]
    public void Eine_Loeschung_verlangt_keinen_Grund() =>
        ConsentEvent.Delete(Anna, Sichtbarkeit, Jetzt, Anna).Reason.Should().BeNull();

    [Fact]
    public void Fremde_Metadaten_kommen_nicht_hinein()
    {
        var mitEmail = () => ConsentEvent.Grant(
            Anna, Sichtbarkeit, Jetzt, Anna,
            metadata: new Dictionary<string, string> { ["email"] = "anna@example.com" });

        mitEmail.Should().Throw<ConsentMetadataException>().Which.Key.Should().Be("email");
    }

    [Fact]
    public void Kein_Ereignis_heisst_nicht_erteilt()
    {
        Projektion.Stand([]).Granted.Should().BeFalse();
        Projektion.Stand([]).Reason.Should().Be("no consent event");
        Projektion.Stand([]).Deleted.Should().BeFalse();
    }

    [Fact]
    public void Das_juengste_Ereignis_entscheidet()
    {
        var erteilt = ConsentEvent.Grant(Anna, Sichtbarkeit, Jetzt, Anna);
        var widerrufen = ConsentEvent.Revoke(
            Anna, Sichtbarkeit, Jetzt.AddMinutes(1), WithdrawalReason.Parse("Ich mag nicht mehr."),
            Anna);

        Projektion.Stand([erteilt, widerrufen]).Granted.Should().BeFalse();
        Projektion.Stand([widerrufen, erteilt]).Granted.Should().BeFalse();
    }

    /// <summary>A withdrawal must never be a one-way door.</summary>
    [Fact]
    public void Nach_einem_Widerruf_darf_neu_erteilt_werden()
    {
        var widerrufen = ConsentEvent.Revoke(
            Anna, Sichtbarkeit, Jetzt, WithdrawalReason.Parse("Erst mal nicht."), Anna);
        var erneut = ConsentEvent.Grant(Anna, Sichtbarkeit, Jetzt.AddDays(30), Anna);

        Projektion.Stand([widerrufen, erneut]).Should().Be(new ConsentState(true));
    }

    /// <summary>
    /// Two facts in the same clock tick resolve deterministically.
    /// </summary>
    /// <remarks>
    /// Without the tie-break the answer would depend on the order rows came
    /// back in, and the same pair could read differently twice in a row.
    /// </remarks>
    [Fact]
    public void Zwei_Ereignisse_im_selben_Takt_loesen_sich_ueber_die_Kennung()
    {
        var kleinereKennung = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var groessereKennung = Guid.Parse("00000000-0000-7000-8000-000000000002");

        var erteilt = ConsentEvent.Restore(
            groessereKennung, Anna, Sichtbarkeit, ConsentAction.Grant, Jetzt, Anna, null, null);
        var widerrufen = ConsentEvent.Restore(
            kleinereKennung, Anna, Sichtbarkeit, ConsentAction.Revoke, Jetzt, Anna,
            WithdrawalReason.Parse("Doch nicht."), null);

        Projektion.Stand([erteilt, widerrufen]).Granted.Should().BeTrue();
        Projektion.Stand([widerrufen, erteilt]).Granted.Should().BeTrue();
    }

    [Fact]
    public void Ein_Widerruf_traegt_seinen_Grund_in_den_Stand()
    {
        var widerrufen = ConsentEvent.Revoke(
            Anna, Sichtbarkeit, Jetzt, WithdrawalReason.Parse("Neue Stelle."), Anna);

        Projektion.Stand([widerrufen]).Should()
            .Be(new ConsentState(false, false, "Neue Stelle."));
    }

    [Fact]
    public void Eine_Loeschung_ist_nicht_dasselbe_wie_ein_Widerruf()
    {
        var geloescht = ConsentEvent.Delete(Anna, Sichtbarkeit, Jetzt, Anna);

        Projektion.Stand([geloescht]).Should().Be(new ConsentState(false, true));
    }

    /// <summary>
    /// Append-only is the shape of the port, not a rule in a document.
    /// </summary>
    [Fact]
    public void Das_Buch_bietet_kein_Aendern_und_kein_Loeschen() =>
        typeof(IConsentLedger).GetMethods().Select(m => m.Name).Should()
            .NotContain(name =>
                name.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Aender", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Loesch", StringComparison.OrdinalIgnoreCase));
}
