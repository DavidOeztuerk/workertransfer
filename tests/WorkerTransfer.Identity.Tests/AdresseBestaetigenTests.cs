using FluentAssertions;
using Girder.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Application.Registrierung;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Confirming an address — and the company intention that rides along with it.
/// </summary>
public class AdresseBestaetigenTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IVerificationTokenRepository _tokens =
        Substitute.For<IVerificationTokenRepository>();
    private readonly IEinmaltoken _einmaltoken = Substitute.For<IEinmaltoken>();
    private readonly ICompanyRepository _firmen = Substitute.For<ICompanyRepository>();
    private readonly IMembershipRepository _mitglieder = Substitute.For<IMembershipRepository>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly Guid TokenId = Guid.CreateVersion7();

    public AdresseBestaetigenTests() => _einmaltoken.Hashe("link").Returns("hash");

    private Task<Bestaetigungsergebnis> Bestaetige(string token = "link") =>
        new AdresseBestaetigenHandler(
            _benutzer, _tokens, _einmaltoken,
            new UnternehmenAnlegen(
                _firmen, _mitglieder, _protokoll,
                new FesteKorrelation("abc-123"), TimeProvider.System),
            _protokoll, new FesteKorrelation("abc-123"), TimeProvider.System)
            .Handle(new AdresseBestaetigenBefehl(token), CancellationToken.None);

    private void EsGibtDenToken(
        DateTimeOffset? verbrauchtAm = null, DateTimeOffset? laeuftAbAm = null) =>
        _tokens.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(new VerificationToken(
                TokenId, Anna, "hash", TokenPurpose.EmailVerify,
                laeuftAbAm ?? DateTimeOffset.UtcNow.AddHours(1), verbrauchtAm));

    private User EsGibtDasKonto(
        AccountStatus status = AccountStatus.Pending,
        string? offeneFirma = null,
        string email = "chef@firma.example")
    {
        var konto = Konten.Bestehend(Anna, email, status: status, offeneFirma: offeneFirma);
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>()).Returns(konto);
        return konto;
    }

    [Fact]
    public async Task Ein_gueltiger_Link_schaltet_das_Konto_frei()
    {
        EsGibtDenToken();
        var konto = EsGibtDasKonto();

        var ergebnis = await Bestaetige();

        ergebnis.Should().BeOfType<Bestaetigungsergebnis.Bestaetigt>();
        konto.Status.Should().Be(AccountStatus.Active);
        await _benutzer.Received().SaveAsync(konto, Arg.Any<CancellationToken>());
        await _tokens.Received(1).ConsumeAsync(
            TokenId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Without the save the unlock stays in memory: the aggregate comes back
    /// detached from the repository. It costs nothing in a test — the
    /// substitute hands back the same instance — and silently loses the write.
    /// </summary>
    [Fact]
    public async Task Die_Freischaltung_wird_ausdruecklich_gespeichert()
    {
        EsGibtDenToken();
        EsGibtDasKonto();

        await Bestaetige();

        await _benutzer.Received().SaveAsync(
            Arg.Is<User>(u => u.Status == AccountStatus.Active), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_unbekannter_Token_ist_ungueltig()
    {
        _tokens.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns((VerificationToken?)null);

        (await Bestaetige()).Should().BeOfType<Bestaetigungsergebnis.Ungueltig>();
    }

    [Fact]
    public async Task Ein_abgelaufener_Link_bekommt_seine_eigene_Antwort()
    {
        EsGibtDenToken(laeuftAbAm: DateTimeOffset.UtcNow.AddHours(-1));
        EsGibtDasKonto();

        (await Bestaetige()).Should().BeOfType<Bestaetigungsergebnis.Abgelaufen>();
        await _benutzer.DidNotReceive().SaveAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The account is exactly as unlocked as the click wanted it. Whoever holds
    /// the token had the mail, so nothing is revealed by saying so.
    /// </summary>
    [Fact]
    public async Task Zweimal_auf_denselben_Link_zu_klicken_ist_kein_Fehler()
    {
        EsGibtDenToken(verbrauchtAm: DateTimeOffset.UtcNow.AddMinutes(-5));
        EsGibtDasKonto(AccountStatus.Active);

        (await Bestaetige()).Should().BeOfType<Bestaetigungsergebnis.Bestaetigt>();
    }

    /// <summary>
    /// Consumed while the account is still pending means the token was voided
    /// by a resend. The old link must not unlock anything any more.
    /// </summary>
    [Fact]
    public async Task Ein_durch_erneutes_Senden_entwerteter_Link_traegt_nicht_mehr()
    {
        EsGibtDenToken(verbrauchtAm: DateTimeOffset.UtcNow.AddMinutes(-5));
        EsGibtDasKonto(AccountStatus.Pending);

        (await Bestaetige()).Should().BeOfType<Bestaetigungsergebnis.Ungueltig>();
    }

    [Fact]
    public async Task Die_Bestaetigung_steht_im_Protokoll()
    {
        EsGibtDenToken();
        EsGibtDasKonto();

        await Bestaetige();

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.EmailVerified && e.Actor == Anna),
            Arg.Any<CancellationToken>());
    }

    public class MitFirmenabsicht : AdresseBestaetigenTests
    {
        /// <summary>
        /// The domain comes from the confirmed address, never from a request —
        /// which is what makes it unforgeable (ADR-0019).
        /// </summary>
        [Fact]
        public async Task Die_Firma_entsteht_erst_jetzt_und_auf_der_bewiesenen_Domain()
        {
            EsGibtDenToken();
            EsGibtDasKonto(offeneFirma: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            var ergebnis = await Bestaetige();

            ergebnis.Should().BeOfType<Bestaetigungsergebnis.Bestaetigt>()
                .Which.Firmenname.Should().Be("Beispiel GmbH");
            await _firmen.Received(1).AddAsync(
                Arg.Is<Company>(f => f.Domain.Value == "firma.example"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Wer_sein_Unternehmen_anlegt_wird_dessen_Administrator()
        {
            EsGibtDenToken();
            EsGibtDasKonto(offeneFirma: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            await Bestaetige();

            await _mitglieder.Received(1).AddAsync(
                Anna, Arg.Any<TenantId>(), MembershipRole.Admin, Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// A state that did not exist before: account confirmed, company
        /// refused. Locking somebody out because a name was taken would be the
        /// wrong answer to the wrong question — so the confirmation stands and
        /// says what happened.
        /// </summary>
        [Fact]
        public async Task Eine_beanspruchte_Domain_verhindert_die_Bestaetigung_nicht()
        {
            EsGibtDenToken();
            var konto = EsGibtDasKonto(offeneFirma: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns(Company.Restore(
                    TenantId.New(), "Andere GmbH",
                    new EmailDomain("firma.example"), TenantStatus.Active));

            var ergebnis = await Bestaetige();

            var bestaetigt = ergebnis.Should().BeOfType<Bestaetigungsergebnis.Bestaetigt>().Which;
            bestaetigt.Firmenfehler.Should().Be("domain_already_claimed");
            bestaetigt.Firmenname.Should().BeNull();
            konto.Status.Should().Be(AccountStatus.Active);
        }

        /// <summary>
        /// Spent in both cases. Left standing it would sit there forever, and a
        /// second click on the same link might create a second company.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Die_Absicht_ist_danach_verbraucht_auch_bei_Ablehnung(bool abgelehnt)
        {
            EsGibtDenToken();
            var konto = EsGibtDasKonto(offeneFirma: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns(abgelehnt
                    ? Company.Restore(
                        TenantId.New(), "Andere GmbH",
                        new EmailDomain("firma.example"), TenantStatus.Active)
                    : null);

            await Bestaetige();

            konto.PendingCompanyName.Should().BeNull();
        }

        /// <summary>
        /// A mass provider cannot be claimed. Registration already refuses it,
        /// so this is the second lock on the same door — and the one that holds
        /// if somebody ever writes the intention another way.
        /// </summary>
        [Fact]
        public async Task Eine_Freemail_Adresse_bekommt_auch_hier_keine_Firma()
        {
            EsGibtDenToken();
            EsGibtDasKonto(offeneFirma: "Beispiel GmbH", email: "chef@gmail.com");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            var ergebnis = await Bestaetige();

            ergebnis.Should().BeOfType<Bestaetigungsergebnis.Bestaetigt>()
                .Which.Firmenfehler.Should().Be("public_email_domain");
            await _firmen.DidNotReceive().AddAsync(
                Arg.Any<Company>(), Arg.Any<CancellationToken>());
        }
    }
}
