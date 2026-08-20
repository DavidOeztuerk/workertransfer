using Girder.Abstractions.Security.Passwords;
using Girder.Core.Identity;
using FluentAssertions;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Signing in: who gets a token, who gets which refusal, and what is spent on
/// an address that does not exist.
/// </summary>
public class AnmeldenTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwoerter = Substitute.For<IPasswordHasher>();
    private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();

    private static readonly SubjectId Anna = SubjectId.New();
    private const string Eintrag = "$2b$12$abcdefghijklmnopqrstuv";

    private AnmeldenHandler Handler() => new(_benutzer, _passwoerter, _sitzungen, _token);

    private void EsGibt(AccountStatus status = AccountStatus.Active) =>
        _benutzer.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>())
            .Returns(new User
            {
                Id = Anna,
                Email = "anna@example.com",
                PasswordHash = Eintrag,
                DisplayName = "Anna",
                Status = status,
                Roles = ["user"]
            });

    private void DasPasswortStimmt(
        PasswordVerification ergebnis = PasswordVerification.Success) =>
        _passwoerter.Verify("geheim", Eintrag).Returns(ergebnis);

    private void EineSitzungEntsteht()
    {
        _sitzungen.StartAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new StartedSession(SessionId.New(), "erneuerungstoken", DateTimeOffset.UtcNow));
        _token.IssueAsync(
                Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
                Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns("zugriffstoken");
    }

    [Fact]
    public async Task Wer_das_Passwort_kennt_bekommt_ein_Tokenpaar()
    {
        EsGibt();
        DasPasswortStimmt();
        EineSitzungEntsteht();

        var ergebnis = await Handler().HandleAsync("anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.Angemeldet>()
            .Which.Zugriffstoken.Should().Be("zugriffstoken");
    }

    /// <summary>
    /// Signing in makes you yourself, never a company. Acting for one is a
    /// second, explicit step that verifies membership first (ADR-0017).
    /// </summary>
    [Fact]
    public async Task Eine_Anmeldung_macht_niemanden_zum_Unternehmen()
    {
        EsGibt();
        DasPasswortStimmt();
        EineSitzungEntsteht();

        await Handler().HandleAsync("anna@example.com", "geheim");

        await _token.Received().IssueAsync(
            Anna, "anna@example.com", Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_falsches_Passwort_ergibt_dieselbe_Absage_wie_eine_unbekannte_Adresse()
    {
        EsGibt();
        DasPasswortStimmt(PasswordVerification.Failed);

        var falsch = await Handler().HandleAsync("anna@example.com", "geheim");
        var unbekannt = await Handler().HandleAsync("niemand@example.com", "geheim");

        falsch.Should().BeOfType<Anmeldeergebnis.Abgelehnt>();
        unbekannt.Should().BeOfType<Anmeldeergebnis.Abgelehnt>();
    }

    /// <summary>
    /// Without this, an unknown address is recognisable by how fast the answer
    /// comes back — bcrypt against a real entry costs a few hundred
    /// milliseconds, and no entry at all costs none.
    /// </summary>
    [Fact]
    public async Task Eine_unbekannte_Adresse_kostet_dieselbe_Rechenarbeit()
    {
        await Handler().HandleAsync("niemand@example.com", "geheim");

        _passwoerter.Received(1).Verify("geheim", null);
    }

    [Fact]
    public async Task Eine_unbekannte_Adresse_beginnt_keine_Sitzung()
    {
        await Handler().HandleAsync("niemand@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_unbestaetigtes_Konto_bekommt_eine_eigene_Absage()
    {
        EsGibt(AccountStatus.Pending);
        DasPasswortStimmt();

        var ergebnis = await Handler().HandleAsync("anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.NichtBestaetigt>();
    }

    /// <summary>
    /// A suspended account is answered like a wrong password. Saying "disabled"
    /// tells whoever guessed the password that the account exists.
    /// </summary>
    [Fact]
    public async Task Ein_gesperrtes_Konto_bekommt_die_gewoehnliche_Absage()
    {
        EsGibt(AccountStatus.Disabled);
        DasPasswortStimmt();

        var ergebnis = await Handler().HandleAsync("anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.Abgelehnt>();
    }

    [Fact]
    public async Task Ein_gesperrtes_Konto_beginnt_keine_Sitzung()
    {
        EsGibt(AccountStatus.Disabled);
        DasPasswortStimmt();

        await Handler().HandleAsync("anna@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The entry is still bcrypt from the Python service. It verifies, and the
    /// sign-in proceeds — see Ü-6 in <c>docs/uebergang-python-dotnet.md</c> for
    /// why nothing is rewritten.
    /// </summary>
    [Fact]
    public async Task Ein_Eintrag_der_neu_geschrieben_werden_duerfte_laesst_trotzdem_herein()
    {
        EsGibt();
        DasPasswortStimmt(PasswordVerification.SuccessRehashNeeded);
        EineSitzungEntsteht();

        var ergebnis = await Handler().HandleAsync("anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.Angemeldet>();
    }
}
