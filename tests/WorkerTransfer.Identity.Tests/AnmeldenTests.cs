using Noelia.Abstractions.Security.Passwords;
using Noelia.Core.Identity;
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
public class SignInTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwoerter = Substitute.For<IPasswordHasher>();
    private readonly ISessionService _sitzungen = Substitute.For<ISessionService>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private const string Entry = "$2b$12$abcdefghijklmnopqrstuv";

    private SignInHandler Handler() => new(
        _benutzer, _passwoerter, _sitzungen, _token, _protokoll,
        new FixedCorrelation("abc-123"), TimeProvider.System);

    private static Task<SignInResult> SignIn(
        SignInHandler handler, string email, string password) =>
        handler.Handle(new SignInCommand(email, password), CancellationToken.None);

    private void ThereIs(AccountStatus status = AccountStatus.Active) =>
        _benutzer.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>())
            .Returns(Accounts.Existing(Anna, "anna@example.com", Entry, status));

    private void ThePasswordIsRight(
        PasswordVerification result = PasswordVerification.Success) =>
        _passwoerter.Verify("geheim", Entry).Returns(result);

    private void ASessionComesIntoBeing()
    {
        _sitzungen.StartAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new StartedSession(SessionId.New(), "erneuerungstoken", DateTimeOffset.UtcNow));
        _token.IssueAsync(
                Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
                Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns("zugriffstoken");
    }

    [Fact]
    public async Task Whoever_knows_the_password_gets_a_token_pair()
    {
        ThereIs();
        ThePasswordIsRight();
        ASessionComesIntoBeing();

        var result = await SignIn(Handler(), "anna@example.com", "geheim");

        result.Should().BeOfType<SignInResult.SignedIn>()
            .Which.AccessToken.Should().Be("zugriffstoken");
    }

    /// <summary>
    /// Signing in makes you yourself, never a company. Acting for one is a
    /// second, explicit step that verifies membership first (ADR-0017).
    /// </summary>
    [Fact]
    public async Task A_sign_in_makes_nobody_a_company()
    {
        ThereIs();
        ThePasswordIsRight();
        ASessionComesIntoBeing();

        await SignIn(Handler(), "anna@example.com", "geheim");

        await _token.Received().IssueAsync(
            Anna, "anna@example.com", Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_wrong_password_gives_the_same_refusal_as_an_unknown_address()
    {
        ThereIs();
        ThePasswordIsRight(PasswordVerification.Failed);

        var wrong = await SignIn(Handler(), "anna@example.com", "geheim");
        var unknown = await SignIn(Handler(), "niemand@example.com", "geheim");

        wrong.Should().BeOfType<SignInResult.Refused>();
        unknown.Should().BeOfType<SignInResult.Refused>();
    }

    /// <summary>
    /// Without this, an unknown address is recognisable by how fast the answer
    /// comes back — bcrypt against a real entry costs a few hundred
    /// milliseconds, and no entry at all costs none.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_costs_the_same_work()
    {
        await SignIn(Handler(), "niemand@example.com", "geheim");

        _passwoerter.Received(1).Verify("geheim", null);
    }

    [Fact]
    public async Task An_unknown_address_begins_no_session()
    {
        await SignIn(Handler(), "niemand@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unconfirmed_account_gets_a_refusal_of_its_own()
    {
        ThereIs(AccountStatus.Pending);
        ThePasswordIsRight();

        var result = await SignIn(Handler(), "anna@example.com", "geheim");

        result.Should().BeOfType<SignInResult.NotConfirmed>();
    }

    /// <summary>
    /// A suspended account is answered like a wrong password. Saying "disabled"
    /// tells whoever guessed the password that the account exists.
    /// </summary>
    [Fact]
    public async Task A_disabled_account_gets_the_ordinary_refusal()
    {
        ThereIs(AccountStatus.Disabled);
        ThePasswordIsRight();

        var result = await SignIn(Handler(), "anna@example.com", "geheim");

        result.Should().BeOfType<SignInResult.Refused>();
    }

    [Fact]
    public async Task A_disabled_account_begins_no_session()
    {
        ThereIs(AccountStatus.Disabled);
        ThePasswordIsRight();

        await SignIn(Handler(), "anna@example.com", "geheim");

        await _sitzungen.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The entry still comes from the Python service's bcrypt hasher. It
    /// verifies, the sign-in goes through, and nobody is asked to reset
    /// anything — that is exactly the point of bcrypt going on WRITING here
    /// (`AddBCryptPasswords`, not only the reader).
    /// </summary>
    [Fact]
    public async Task An_entry_that_could_be_rehashed_still_lets_one_in()
    {
        ThereIs();
        ThePasswordIsRight(PasswordVerification.SuccessRehashNeeded);
        ASessionComesIntoBeing();

        var result = await SignIn(Handler(), "anna@example.com", "geheim");

        result.Should().BeOfType<SignInResult.SignedIn>();
    }

    /// <summary>
    /// Every refusal is in the trail, and the reason is only there. The answer
    /// is the same for all four.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Active, PasswordVerification.Failed, "bad_password")]
    [InlineData(AccountStatus.Pending, PasswordVerification.Success, "email_not_confirmed")]
    [InlineData(AccountStatus.Disabled, PasswordVerification.Success, "disabled")]
    public async Task Every_refusal_names_its_reason_in_the_trail(
        AccountStatus status, PasswordVerification verdict, string reason)
    {
        ThereIs(status);
        ThePasswordIsRight(verdict);

        await SignIn(Handler(), "anna@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginFailure
                                    && e.Metadata["reason"] == reason
                                    && e.Actor == Anna),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// There is no actor to name, and inventing one would be a claim about
    /// somebody.
    /// </summary>
    [Fact]
    public async Task An_unknown_address_is_recorded_without_an_actor()
    {
        await SignIn(Handler(), "niemand@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginFailure
                                    && e.Actor == null
                                    && e.Metadata["reason"] == "unknown_user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_successful_sign_in_stands_in_the_trail()
    {
        ThereIs();
        ThePasswordIsRight();
        ASessionComesIntoBeing();

        await SignIn(Handler(), "anna@example.com", "geheim");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.LoginSuccess
                                    && e.Actor == Anna
                                    && e.CorrelationId == "abc-123"),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>A correlation id that does not need a request.</summary>
internal sealed class FixedCorrelation(string? value) : ICorrelation
{
    public string? Current { get; } = value;
}
