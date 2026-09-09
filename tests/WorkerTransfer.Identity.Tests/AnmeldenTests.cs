using Girder.Abstractions.Security.Passwords;
using Girder.Core.Identity;
using FluentAssertions;
using NSubstitute;
using WorkerTransfer.Identity.Application.Anmelden;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
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
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private const string Eintrag = "$2b$12$abcdefghijklmnopqrstuv";

    private AnmeldenHandler Handler() => new(
        _benutzer, _passwoerter, _sitzungen, _token, _protokoll,
        new FesteKorrelation("abc-123"), TimeProvider.System);

    private static Task<Anmeldeergebnis> Anmelden(
        AnmeldenHandler handler, string email, string passwort) =>
        handler.Handle(new AnmeldenBefehl(email, passwort), CancellationToken.None);

    private void EsGibt(AccountStatus status = AccountStatus.Active) =>
        _benutzer.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>())
            .Returns(Konten.Bestehend(Anna, "anna@example.com", Eintrag, status));

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

        var ergebnis = await Anmelden(Handler(), "anna@example.com", "geheim");

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

        await Anmelden(Handler(), "anna@example.com", "geheim");

        await _token.Received().IssueAsync(
            Anna, "anna@example.com", Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_falsches_Passwort_ergibt_dieselbe_Absage_wie_eine_unbekannte_Adresse()
    {
        EsGibt();
        DasPasswortStimmt(PasswordVerification.Failed);

        var falsch = await Anmelden(Handler(), "anna@example.com", "geheim");
        var unbekannt = await Anmelden(Handler(), "niemand@example.com", "geheim");

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
        await Anmelden(Handler(), "niemand@example.com", "geheim");

        _passwoerter.Received(1).Verify("geheim", null);
    }

    [Fact]
    public async Task Eine_unbekannte_Adresse_beginnt_keine_Sitzung()
    {
        await Anmelden(Handler(), "niemand@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ein_unbestaetigtes_Konto_bekommt_eine_eigene_Absage()
    {
        EsGibt(AccountStatus.Pending);
        DasPasswortStimmt();

        var ergebnis = await Anmelden(Handler(), "anna@example.com", "geheim");

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

        var ergebnis = await Anmelden(Handler(), "anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.Abgelehnt>();
    }

    [Fact]
    public async Task Ein_gesperrtes_Konto_beginnt_keine_Sitzung()
    {
        EsGibt(AccountStatus.Disabled);
        DasPasswortStimmt();

        await Anmelden(Handler(), "anna@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Der Eintrag stammt noch vom bcrypt-Hasher des Python-Dienstes. Er
    /// verifiziert, die Anmeldung geht durch, und niemand wird gebeten, etwas
    /// zurueckzusetzen — genau das ist der Sinn, mit dem bcrypt hier weiter
    /// SCHREIBT (`AddBCryptPasswords`, nicht nur der Leser).
    /// </summary>
    [Fact]
    public async Task Ein_Eintrag_der_neu_geschrieben_werden_duerfte_laesst_trotzdem_herein()
    {
        EsGibt();
        DasPasswortStimmt(PasswordVerification.SuccessRehashNeeded);
        EineSitzungEntsteht();

        var ergebnis = await Anmelden(Handler(), "anna@example.com", "geheim");

        ergebnis.Should().BeOfType<Anmeldeergebnis.Angemeldet>();
    }

    /// <summary>
    /// Every refusal is in the trail, and the reason is only there. The answer
    /// is the same for all four.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Active, PasswordVerification.Failed, "bad_password")]
    [InlineData(AccountStatus.Pending, PasswordVerification.Success, "email_not_confirmed")]
    [InlineData(AccountStatus.Disabled, PasswordVerification.Success, "disabled")]
    public async Task Jede_Absage_nennt_ihren_Grund_im_Protokoll(
        AccountStatus status, PasswordVerification urteil, string grund)
    {
        EsGibt(status);
        DasPasswortStimmt(urteil);

        await Anmelden(Handler(), "anna@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginFailure
                                    && e.Metadata["reason"] == grund
                                    && e.Actor == Anna),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// There is no actor to name, and inventing one would be a claim about
    /// somebody.
    /// </summary>
    [Fact]
    public async Task Eine_unbekannte_Adresse_wird_ohne_Handelnden_protokolliert()
    {
        await Anmelden(Handler(), "niemand@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginFailure
                                    && e.Actor == null
                                    && e.Metadata["reason"] == "unknown_user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Eine_gelungene_Anmeldung_steht_im_Protokoll()
    {
        EsGibt();
        DasPasswortStimmt();
        EineSitzungEntsteht();

        await Anmelden(Handler(), "anna@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginSuccess
                                    && e.Actor == Anna
                                    && e.CorrelationId == "abc-123"),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>A correlation id that does not need a request.</summary>
internal sealed class FesteKorrelation(string? wert) : IKorrelation
{
    public string? Aktuell { get; } = wert;
}
