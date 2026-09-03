using FluentAssertions;
using Girder.Abstractions.Security.Passwords;
using Girder.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Application.Registrierung;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Registering: who gets an account, who gets told, and what an address that
/// already exists costs.
/// </summary>
public class RegistrierenTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwoerter = Substitute.For<IPasswordHasher>();
    private readonly IVerificationTokenRepository _tokens =
        Substitute.For<IVerificationTokenRepository>();
    private readonly IEinmaltoken _einmaltoken = Substitute.For<IEinmaltoken>();
    private readonly IPostkorb _postkorb = Substitute.For<IPostkorb>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private const string Passwort = "geheim-und-lang-genug";

    public RegistrierenTests()
    {
        _passwoerter.Hash(Arg.Any<string>()).Returns("$2b$12$neu");
        _einmaltoken.Erzeuge().Returns(("klartext", "hash"));
    }

    private Task<Registrierergebnis> Registriere(
        string email = "anna@example.com",
        string passwort = Passwort,
        string? firma = null) =>
        new RegistrierenHandler(
            _benutzer, _passwoerter, _tokens, _einmaltoken, _postkorb, _protokoll,
            new FesteKorrelation("abc-123"), TimeProvider.System)
            .Handle(
                new RegistrierenBefehl(email, passwort, "Anna", firma), CancellationToken.None);

    private void EsGibtBereits(string email)
    {
        var vorhanden = Konten.Bestehend(SubjectId.New(), email);
        _benutzer.FindByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(vorhanden);
    }

    [Fact]
    public async Task Eine_neue_Adresse_bekommt_ein_Konto_und_einen_Bestaetigungslink()
    {
        var ergebnis = await Registriere();

        ergebnis.Should().BeOfType<Registrierergebnis.Angenommen>();
        await _benutzer.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _tokens.Received(1).AddAsync(
            Arg.Is<VerificationToken>(t => t.TokenHash == "hash"
                                           && t.Purpose == TokenPurpose.EmailVerify
                                           && t.ConsumedAt == null),
            Arg.Any<CancellationToken>());
        _postkorb.Received(1).Bestaetigungslink(
            "anna@example.com", Arg.Any<SubjectId>(), "klartext",
            Arg.Any<Kontosprache>());
    }

    /// <summary>
    /// The same answer for both, and it is one result type so no endpoint can
    /// accidentally tell them apart. A 409 would answer "is this person here?"
    /// without asking the consent ledger.
    /// </summary>
    [Fact]
    public async Task Eine_vergebene_Adresse_bekommt_dieselbe_Antwort_wie_eine_freie()
    {
        EsGibtBereits("besetzt@example.com");

        var frei = await Registriere("frei@example.com");
        var besetzt = await Registriere("besetzt@example.com");

        frei.Should().BeOfType<Registrierergebnis.Angenommen>();
        besetzt.Should().BeOfType<Registrierergebnis.Angenommen>();
    }

    /// <summary>
    /// Nothing is written and no link goes out — but the real owner is told,
    /// which is the only thing that happens.
    /// </summary>
    [Fact]
    public async Task Eine_vergebene_Adresse_warnt_den_echten_Besitzer()
    {
        EsGibtBereits("besetzt@example.com");

        await Registriere("besetzt@example.com");

        await _benutzer.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _tokens.DidNotReceive().AddAsync(
            Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>());
        _postkorb.Received(1).Doppelanmeldung(
            "besetzt@example.com", Arg.Any<SubjectId>(), Arg.Any<Kontosprache>());
        _postkorb.DidNotReceive().Bestaetigungslink(
            Arg.Any<string>(), Arg.Any<SubjectId>(), Arg.Any<string>(),
            Arg.Any<Kontosprache>());
    }

    /// <summary>
    /// bcrypt with twelve rounds costs a few hundred milliseconds; an early
    /// exit would be back in ten and would give away over the clock what the
    /// identical answer is hiding.
    /// </summary>
    [Fact]
    public async Task Eine_vergebene_Adresse_kostet_dieselbe_Rechenarbeit()
    {
        EsGibtBereits("besetzt@example.com");

        await Registriere("besetzt@example.com");

        _passwoerter.Received(1).Hash(Passwort);
    }

    /// <summary>
    /// Checked BEFORE the existence check, and that is not a matter of style:
    /// afterwards, a known freemail address would get the silent "ok" and an
    /// unknown one a 422 — and that difference is the enumeration channel this
    /// endpoint exists to close.
    /// </summary>
    [Fact]
    public async Task Freemail_wird_vor_der_Existenzpruefung_abgewiesen()
    {
        EsGibtBereits("chef@gmail.com");

        var bekannt = await Registriere("chef@gmail.com", firma: "Beispiel GmbH");
        var unbekannt = await Registriere("neu@gmail.com", firma: "Beispiel GmbH");

        bekannt.Should().BeOfType<Registrierergebnis.OeffentlicheDomain>();
        unbekannt.Should().BeOfType<Registrierergebnis.OeffentlicheDomain>();
        _passwoerter.DidNotReceive().Hash(Arg.Any<string>());
    }

    /// <summary>
    /// A private address is the ordinary case on a transfer market. The
    /// blocklist applies at exactly one place: claiming a domain as a company.
    /// </summary>
    [Fact]
    public async Task Ohne_Firmenwunsch_ist_eine_Freemail_Adresse_voellig_in_Ordnung()
    {
        var ergebnis = await Registriere("anna@gmail.com");

        ergebnis.Should().BeOfType<Registrierergebnis.Angenommen>();
        await _benutzer.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Asked here it would answer "is firma.de on this platform?" to anyone who
    /// guesses a domain. It waits for the confirmation, when the address is
    /// proven.
    /// </summary>
    [Fact]
    public async Task Ob_die_Domain_vergeben_ist_wird_hier_nicht_gefragt()
    {
        var ergebnis = await Registriere("chef@firma.example", firma: "Beispiel GmbH");

        ergebnis.Should().BeOfType<Registrierergebnis.Angenommen>();
        await _benutzer.Received(1).AddAsync(
            Arg.Is<User>(u => u.PendingCompanyName == "Beispiel GmbH"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_neues_Konto_ist_unbestaetigt_und_traegt_keine_Firma()
    {
        await Registriere();

        await _benutzer.Received(1).AddAsync(
            Arg.Is<User>(u => u.Status == AccountStatus.Pending
                              && u.PendingCompanyName == null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData("elfzeichen")]
    public async Task Ein_zu_kurzes_Passwort_wird_abgewiesen(string passwort)
    {
        var ergebnis = await Registriere(passwort: passwort);

        ergebnis.Should().BeOfType<Registrierergebnis.SchwachesPasswort>();
        await _benutzer.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// bcrypt ignores everything past the 72nd byte. Accepting a longer one
    /// would let the tail protect nothing while the person believes it does.
    /// </summary>
    [Fact]
    public async Task Ein_Passwort_ueber_72_Byte_wird_abgewiesen()
    {
        var ergebnis = await Registriere(passwort: new string('ä', 40));

        ergebnis.Should().BeOfType<Registrierergebnis.SchwachesPasswort>();
    }

    [Fact]
    public async Task Die_Registrierung_steht_im_Protokoll_und_traegt_keine_Firma()
    {
        await Registriere("chef@firma.example", firma: "Beispiel GmbH");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.Register
                                    && e.Tenant == null
                                    && e.CorrelationId == "abc-123"),
            Arg.Any<CancellationToken>());
    }
}
