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

/// <summary>
/// Acting for a company: the caller names it, the membership decides.
/// </summary>
public class FirmaWechselnTests
{
    private readonly IMembershipRepository _mitglieder = Substitute.For<IMembershipRepository>();
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
    private readonly ISessionCapacity _form = Substitute.For<ISessionCapacity>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly TenantId Firma = TenantId.New();
    private static readonly SessionId Sitzung = SessionId.New();

    private Task<Firmenwechselergebnis> Wechseln() =>
        new FirmaWechselnHandler(
            _mitglieder, _benutzer, _sitzungen, _form, _token, _protokoll,
            new FesteKorrelation("abc-123"), TimeProvider.System)
            .Handle(new FirmaWechselnBefehl(Anna, Firma), CancellationToken.None);

    private void EsGibtDasKonto(AccountStatus status = AccountStatus.Active) =>
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new User
            {
                Id = Anna,
                Email = "anna@example.com",
                PasswordHash = "$2b$12$x",
                DisplayName = "Anna",
                Status = status,
                Roles = ["user"]
            });

    private void EineSitzungEntsteht()
    {
        _sitzungen.StartAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new StartedSession(Sitzung, "erneuerung", DateTimeOffset.UtcNow.AddDays(1)));
        _token.IssueAsync(
                Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
                Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns("zugriff");
    }

    [Fact]
    public async Task Ein_Mitglied_bekommt_ein_Paar_das_fuer_die_Firma_handelt()
    {
        _mitglieder.IsMemberAsync(Anna, Firma, Arg.Any<CancellationToken>()).Returns(true);
        EsGibtDasKonto();
        EineSitzungEntsteht();

        var ergebnis = await Wechseln();

        ergebnis.Should().BeOfType<Firmenwechselergebnis.Gewechselt>();
        await _token.Received().IssueAsync(
            Anna, "anna@example.com",
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(Firma))),
            Sitzung, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The whole point: the tenant that reaches a token was never taken from
    /// the request, it was derived from a checked membership.
    /// </summary>
    [Fact]
    public async Task Ohne_Mitgliedschaft_wird_kein_Token_ausgestellt()
    {
        _mitglieder.IsMemberAsync(Anna, Firma, Arg.Any<CancellationToken>()).Returns(false);

        var ergebnis = await Wechseln();

        ergebnis.Should().BeOfType<Firmenwechselergebnis.KeinMitglied>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The company the caller asked for is the point of the record — especially
    /// because it was refused.
    /// </summary>
    [Fact]
    public async Task Eine_abgewiesene_Anfrage_nennt_die_Firma_im_Protokoll()
    {
        _mitglieder.IsMemberAsync(Anna, Firma, Arg.Any<CancellationToken>()).Returns(false);

        await Wechseln();

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.TenantSwitchDenied
                                    && e.Actor == Anna
                                    && e.Tenant == Firma),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A fresh sign-in, not an edit of the old one: the personal refresh token
    /// keeps working, and the company-bound one can be ended on its own.
    /// </summary>
    [Fact]
    public async Task Der_Wechsel_beginnt_eine_eigene_Sitzung_und_merkt_sich_die_Firma()
    {
        _mitglieder.IsMemberAsync(Anna, Firma, Arg.Any<CancellationToken>()).Returns(true);
        EsGibtDasKonto();
        EineSitzungEntsteht();

        await Wechseln();

        await _sitzungen.Received(1).StartAsync(Anna, Arg.Any<CancellationToken>());
        await _form.Received(1).RememberAsync(
            Sitzung,
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(Firma))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Pending)]
    public async Task Ein_Konto_das_nicht_handeln_darf_wechselt_auch_nicht(AccountStatus status)
    {
        _mitglieder.IsMemberAsync(Anna, Firma, Arg.Any<CancellationToken>()).Returns(true);
        EsGibtDasKonto(status);

        var ergebnis = await Wechseln();

        ergebnis.Should().BeOfType<Firmenwechselergebnis.Abgelehnt>();
        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }
}
