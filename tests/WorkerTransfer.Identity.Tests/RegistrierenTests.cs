using FluentAssertions;
using Noelia.Abstractions.Security.Passwords;
using Noelia.Core.Identity;
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
public class RegisterTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwoerter = Substitute.For<IPasswordHasher>();
    private readonly IVerificationTokenRepository _tokens =
        Substitute.For<IVerificationTokenRepository>();
    private readonly IOneTimeToken _einmaltoken = Substitute.For<IOneTimeToken>();
    private readonly IMailTray _postkorb = Substitute.For<IMailTray>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private const string Password = "geheim-und-lang-genug";

    public RegisterTests()
    {
        _passwoerter.Hash(Arg.Any<string>()).Returns("$2b$12$neu");
        _einmaltoken.Create().Returns(("klartext", "hash"));
    }

    private Task<RegistrationResult> Register(
        string email = "anna@example.com",
        string password = Password,
        string? company = null) =>
        new RegisterHandler(
            _benutzer, _passwoerter, _tokens, _einmaltoken, _postkorb, _protokoll,
            new FixedCorrelation("abc-123"), TimeProvider.System)
            .Handle(
                new RegisterCommand(email, password, "Anna", company), CancellationToken.None);

    private void ThereAlreadyIs(string email)
    {
        var existing = Accounts.Existing(SubjectId.New(), email);
        _benutzer.FindByEmailAsync(email, Arg.Any<CancellationToken>()).Returns(existing);
    }

    [Fact]
    public async Task A_new_address_gets_an_account_and_a_confirmation_link()
    {
        var result = await Register();

        result.Should().BeOfType<RegistrationResult.Accepted>();
        await _benutzer.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _tokens.Received(1).AddAsync(
            Arg.Is<VerificationToken>(t => t.TokenHash == "hash"
                                           && t.Purpose == TokenPurpose.EmailVerify
                                           && t.ConsumedAt == null),
            Arg.Any<CancellationToken>());
        _postkorb.Received(1).ConfirmationLink(
            "anna@example.com", Arg.Any<SubjectId>(), "klartext",
            Arg.Any<AccountLanguage>());
    }

    /// <summary>
    /// The same answer for both, and it is one result type so no endpoint can
    /// accidentally tell them apart. A 409 would answer "is this person here?"
    /// without asking the consent ledger.
    /// </summary>
    [Fact]
    public async Task A_taken_address_gets_the_same_answer_as_a_free_one()
    {
        ThereAlreadyIs("besetzt@example.com");

        var free = await Register("frei@example.com");
        var taken = await Register("besetzt@example.com");

        free.Should().BeOfType<RegistrationResult.Accepted>();
        taken.Should().BeOfType<RegistrationResult.Accepted>();
    }

    /// <summary>
    /// Nothing is written and no link goes out — but the real owner is told,
    /// which is the only thing that happens.
    /// </summary>
    [Fact]
    public async Task A_taken_address_warns_the_real_owner()
    {
        ThereAlreadyIs("besetzt@example.com");

        await Register("besetzt@example.com");

        await _benutzer.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _tokens.DidNotReceive().AddAsync(
            Arg.Any<VerificationToken>(), Arg.Any<CancellationToken>());
        _postkorb.Received(1).DuplicateRegistration(
            "besetzt@example.com", Arg.Any<SubjectId>(), Arg.Any<AccountLanguage>());
        _postkorb.DidNotReceive().ConfirmationLink(
            Arg.Any<string>(), Arg.Any<SubjectId>(), Arg.Any<string>(),
            Arg.Any<AccountLanguage>());
    }

    /// <summary>
    /// bcrypt with twelve rounds costs a few hundred milliseconds; an early
    /// exit would be back in ten and would give away over the clock what the
    /// identical answer is hiding.
    /// </summary>
    [Fact]
    public async Task A_taken_address_costs_the_same_work()
    {
        ThereAlreadyIs("besetzt@example.com");

        await Register("besetzt@example.com");

        _passwoerter.Received(1).Hash(Password);
    }

    /// <summary>
    /// Checked BEFORE the existence check, and that is not a matter of style:
    /// afterwards, a known freemail address would get the silent "ok" and an
    /// unknown one a 422 — and that difference is the enumeration channel this
    /// endpoint exists to close.
    /// </summary>
    [Fact]
    public async Task Freemail_is_refused_before_the_existence_check()
    {
        ThereAlreadyIs("chef@gmail.com");

        var known = await Register("chef@gmail.com", company: "Beispiel GmbH");
        var unknown = await Register("neu@gmail.com", company: "Beispiel GmbH");

        known.Should().BeOfType<RegistrationResult.PublicDomain>();
        unknown.Should().BeOfType<RegistrationResult.PublicDomain>();
        _passwoerter.DidNotReceive().Hash(Arg.Any<string>());
    }

    /// <summary>
    /// A private address is the ordinary case on a transfer market. The
    /// blocklist applies at exactly one place: claiming a domain as a company.
    /// </summary>
    [Fact]
    public async Task Without_a_company_wish_a_freemail_address_is_perfectly_fine()
    {
        var result = await Register("anna@gmail.com");

        result.Should().BeOfType<RegistrationResult.Accepted>();
        await _benutzer.Received(1).AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Asked here it would answer "is firma.de on this platform?" to anyone who
    /// guesses a domain. It waits for the confirmation, when the address is
    /// proven.
    /// </summary>
    [Fact]
    public async Task Whether_the_domain_is_taken_is_not_asked_here()
    {
        var result = await Register("chef@firma.example", company: "Beispiel GmbH");

        result.Should().BeOfType<RegistrationResult.Accepted>();
        await _benutzer.Received(1).AddAsync(
            Arg.Is<User>(u => u.PendingCompanyName == "Beispiel GmbH"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_new_account_is_unconfirmed_and_carries_no_company()
    {
        await Register();

        await _benutzer.Received(1).AddAsync(
            Arg.Is<User>(u => u.Status == AccountStatus.Pending
                              && u.PendingCompanyName == null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData("elfzeichen")]
    public async Task A_password_that_is_too_short_is_refused(string password)
    {
        var result = await Register(password: password);

        result.Should().BeOfType<RegistrationResult.WeakPassword>();
        await _benutzer.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// bcrypt ignores everything past the 72nd byte. Accepting a longer one
    /// would let the tail protect nothing while the person believes it does.
    /// </summary>
    [Fact]
    public async Task A_password_over_72_bytes_is_refused()
    {
        var result = await Register(password: new string('ä', 40));

        result.Should().BeOfType<RegistrationResult.WeakPassword>();
    }

    [Fact]
    public async Task The_registration_stands_in_the_trail_and_carries_no_company()
    {
        await Register("chef@firma.example", company: "Beispiel GmbH");

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.Register
                                    && e.Tenant == null
                                    && e.CorrelationId == "abc-123"),
            Arg.Any<CancellationToken>());
    }
}
