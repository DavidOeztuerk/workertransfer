using FluentAssertions;
using Girder.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Carrying a sign-in forward, and ending it.</summary>
public class ErneuernUndAbmeldenTests
{
    private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly SessionId Sitzung = SessionId.New();

    private ErneuernHandler Erneuern() => new(_sitzungen, _benutzer, _token);

    private void DieSitzungTraegtNoch() =>
        _sitzungen.RenewAsync("alt", Arg.Any<CancellationToken>())
            .Returns(new RenewedSession(Sitzung, Anna, "neu", DateTimeOffset.UtcNow.AddDays(1)));

    private void EsGibtDasKonto() =>
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new User
            {
                Id = Anna,
                Email = "anna@example.com",
                PasswordHash = "$2b$12$x",
                DisplayName = "Anna",
                Status = AccountStatus.Active,
                Roles = ["user"]
            });

    [Fact]
    public async Task Ein_gueltiger_Erneuerungstoken_ergibt_ein_frisches_Paar()
    {
        DieSitzungTraegtNoch();
        EsGibtDasKonto();
        _token.IssueAsync(Anna, "anna@example.com", Arg.Any<Capacity>(), Sitzung,
            Arg.Any<CancellationToken>()).Returns("frisch");

        var ergebnis = await Erneuern().HandleAsync("alt");

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
        EsGibtDasKonto();

        await Erneuern().HandleAsync("alt");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(), Arg.Any<Capacity>(), Sitzung, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_verbrauchter_oder_unbekannter_Token_wird_abgewiesen()
    {
        _sitzungen.RenewAsync("tot", Arg.Any<CancellationToken>()).Returns((RenewedSession?)null);

        var ergebnis = await Erneuern().HandleAsync("tot");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Abgewiesen>();
    }

    [Fact]
    public async Task Ohne_Konto_wird_kein_Token_ausgestellt()
    {
        DieSitzungTraegtNoch();
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>()).Returns((User?)null);

        var ergebnis = await Erneuern().HandleAsync("alt");

        ergebnis.Should().BeOfType<Erneuerungsergebnis.Abgewiesen>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Signing in gives a person token; so does refreshing. Acting for a
    /// company is a step of its own — see Ü-5 in
    /// <c>docs/uebergang-python-dotnet.md</c>.
    /// </summary>
    [Fact]
    public async Task Eine_Erneuerung_macht_niemanden_zum_Unternehmen()
    {
        DieSitzungTraegtNoch();
        EsGibtDasKonto();

        await Erneuern().HandleAsync("alt");

        await _token.Received().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(),
            Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    public class Abmelden
    {
        private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();

        [Fact]
        public async Task Beendet_die_Sitzung_zum_vorgelegten_Token()
        {
            await new AbmeldenHandler(_sitzungen).HandleAsync("meiner");

            await _sitzungen.Received().EndByRefreshTokenAsync(
                "meiner", Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Ohne_Token_passiert_nichts_und_niemand_erfaehrt_davon(string? keiner)
        {
            var abmelden = () => new AbmeldenHandler(_sitzungen).HandleAsync(keiner);

            await abmelden.Should().NotThrowAsync();
            await _sitzungen.DidNotReceive().EndByRefreshTokenAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }
}
