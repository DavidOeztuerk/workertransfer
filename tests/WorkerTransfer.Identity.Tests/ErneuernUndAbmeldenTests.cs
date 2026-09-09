using FluentAssertions;
using Girder.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Carrying a sign-in forward, and ending it.</summary>
public class ErneuernUndAbmeldenTests
{
    private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();
    private readonly IMembershipRepository _mitglieder = Substitute.For<IMembershipRepository>();
    private readonly ISessionCapacity _form = Substitute.For<ISessionCapacity>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly SessionId Sitzung = SessionId.New();

    private ErneuernHandler Erneuern() => new(
        _sitzungen, _benutzer, _mitglieder, _form, _token, _protokoll,
        new FesteKorrelation("abc-123"), TimeProvider.System);

    private Task<Erneuerungsergebnis> Erneuern(string token) =>
        Erneuern().Handle(new ErneuernBefehl(token), CancellationToken.None);

    private void HandeltAlsPrivatperson() =>
        _form.RecallAsync(Sitzung, Arg.Any<CancellationToken>())
            .Returns(Capacity.AsSelf.Instance);

    private void HandeltFuer(TenantId firma) =>
        _form.RecallAsync(Sitzung, Arg.Any<CancellationToken>())
            .Returns(new Capacity.ForCompany(firma));

    private void DieSitzungTraegtNoch() =>
        _sitzungen.RenewAsync("alt", Arg.Any<CancellationToken>())
            .Returns(new RenewedSession(Sitzung, Anna, "neu", DateTimeOffset.UtcNow.AddDays(1)));

    private void EsGibtDasKonto() =>
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Konten.Bestehend(Anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

    [Fact]
    public async Task Ein_gueltiger_Erneuerungstoken_ergibt_ein_frisches_Paar()
    {
        DieSitzungTraegtNoch();
        HandeltAlsPrivatperson();
        EsGibtDasKonto();
        _token.IssueAsync(Anna, "anna@example.com", Arg.Any<Capacity>(), Sitzung,
            Arg.Any<CancellationToken>()).Returns("frisch");

        var ergebnis = await Erneuern("alt");

        var erneuert = ergebnis.Should().BeOfType<Erneuerungsergebnis.Erneuert>().Which;
        erneuert.Zugriffstoken.Should().Be("frisch");
        erneuert.Erneuerungstoken.Should().Be("neu");
    }

    /// <summary>
    /// The sign-in keeps its identity across every rotation, so a revocation
    /// can still name the device it belongs to.
    /// </summary>
    [Fact]
    public async Task Die_Sitzung_bleibt_dieselbe()
    {
        DieSitzungTraegtNoch();
        HandeltAlsPrivatperson();
        EsGibtDasKonto();

        await Erneuern("alt");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(), Arg.Any<Capacity>(), Sitzung, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_verbrauchter_oder_unbekannter_Token_wird_abgewiesen()
    {
        _sitzungen.RenewAsync("tot", Arg.Any<CancellationToken>()).Returns((RenewedSession?)null);

        var ergebnis = await Erneuern("tot");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Abgewiesen>();
    }

    [Fact]
    public async Task Ohne_Konto_wird_kein_Token_ausgestellt()
    {
        DieSitzungTraegtNoch();
        HandeltAlsPrivatperson();
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>()).Returns((User?)null);

        var ergebnis = await Erneuern("alt");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Abgewiesen>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Signing in gives a person token; so does refreshing. Acting for a
    /// company is a step of its own — see <c>ISessionCapacity</c>.
    /// </summary>
    [Fact]
    public async Task Eine_Erneuerung_macht_niemanden_zum_Unternehmen()
    {
        DieSitzungTraegtNoch();
        HandeltAlsPrivatperson();
        EsGibtDasKonto();

        await Erneuern("alt");

        await _token.Received().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(),
            Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Checked again on every refresh, never carried forward. Checked once, the
    /// check is good exactly once — and whoever was let in stays in for as long
    /// as they keep refreshing.
    /// </summary>
    [Fact]
    public async Task Eine_erloschene_Mitgliedschaft_faellt_bei_der_Erneuerung_weg()
    {
        var firma = TenantId.New();
        DieSitzungTraegtNoch();
        EsGibtDasKonto();
        HandeltFuer(firma);
        _mitglieder.IsMemberAsync(Anna, firma, Arg.Any<CancellationToken>()).Returns(false);

        await Erneuern("alt");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(), Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.TenantSwitchDenied
                                    && e.Tenant == firma
                                    && e.Metadata["reason"] == "membership_gone_on_refresh"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Only the company falls away, never the sign-in. The person is still
    /// signed in and acts as themselves again (ADR-0017); ending the session
    /// would sign someone out of their personal account because a company
    /// relationship ended.
    /// </summary>
    [Fact]
    public async Task Eine_erloschene_Mitgliedschaft_beendet_die_Sitzung_nicht()
    {
        var firma = TenantId.New();
        DieSitzungTraegtNoch();
        EsGibtDasKonto();
        HandeltFuer(firma);
        _mitglieder.IsMemberAsync(Anna, firma, Arg.Any<CancellationToken>()).Returns(false);

        var ergebnis = await Erneuern("alt");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Erneuert>();
        await _sitzungen.DidNotReceive().EndAsync(
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eine_bestehende_Mitgliedschaft_bleibt_ueber_die_Erneuerung_hinweg()
    {
        var firma = TenantId.New();
        DieSitzungTraegtNoch();
        EsGibtDasKonto();
        HandeltFuer(firma);
        _mitglieder.IsMemberAsync(Anna, firma, Arg.Any<CancellationToken>()).Returns(true);

        await Erneuern("alt");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(),
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(firma))),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A refresh mints a fresh access token, so it is an authorisation decision
    /// and not a formality. Without this, disabling an account leaves the
    /// person working for as long as they keep refreshing.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Pending)]
    public async Task Ein_Konto_das_sich_nicht_anmelden_darf_wird_auch_nicht_erneuert(
        AccountStatus status)
    {
        DieSitzungTraegtNoch();
        HandeltAlsPrivatperson();
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Konten.Bestehend(Anna, "anna@example.com", "$2b$12$x", status));

        var ergebnis = await Erneuern("alt");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Abgewiesen>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    public class Abmelden
    {
        private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
        private readonly ISessionCapacity _form = Substitute.For<ISessionCapacity>();
        private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

        private static readonly SubjectId Anna = SubjectId.New();
        private static readonly SessionId Sitzung = SessionId.New();

        private AbmeldenHandler Handler() => new(
            _sitzungen, _form, _protokoll, new FesteKorrelation(null), TimeProvider.System);

        private Task Abmelden_(string? token) =>
            Handler().Handle(new AbmeldenBefehl(token), CancellationToken.None);

        [Fact]
        public async Task Beendet_die_Sitzung_zum_vorgelegten_Token()
        {
            _sitzungen.EndByRefreshTokenAsync("meiner", Arg.Any<CancellationToken>())
                .Returns(new BeendeteSitzung(Sitzung, Anna));

            await Abmelden_("meiner");

            await _sitzungen.Received().EndByRefreshTokenAsync(
                "meiner", Arg.Any<CancellationToken>());
            await _protokoll.Received(1).AppendAsync(
                Arg.Is<AuditEvent>(e => e.Action == AuditAction.TokenRevoke && e.Actor == Anna),
                Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// The sign-in is over, so what it acted as is no longer an answer to
        /// anything — and a row naming a company on a session that no longer
        /// exists is worse than none.
        /// </summary>
        [Fact]
        public async Task Vergisst_wofuer_die_Sitzung_gehandelt_hat()
        {
            _sitzungen.EndByRefreshTokenAsync("meiner", Arg.Any<CancellationToken>())
                .Returns(new BeendeteSitzung(Sitzung, Anna));

            await Abmelden_("meiner");

            await _form.Received(1).ForgetAsync(Sitzung, Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Ohne_Token_passiert_nichts_und_niemand_erfaehrt_davon(string? keiner)
        {
            var abmelden = () => Abmelden_(keiner);

            await abmelden.Should().NotThrowAsync();
            await _sitzungen.DidNotReceive().EndByRefreshTokenAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// A token that named no sign-in leaves nothing to record. Writing an
        /// entry anyway would put a sign-out in the trail that never happened.
        /// </summary>
        [Fact]
        public async Task Ein_toter_Token_erzeugt_keinen_Protokolleintrag()
        {
            _sitzungen.EndByRefreshTokenAsync("tot", Arg.Any<CancellationToken>())
                .Returns((BeendeteSitzung?)null);

            await Abmelden_("tot");

            await _protokoll.DidNotReceive().AppendAsync(
                Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
        }
    }
}
