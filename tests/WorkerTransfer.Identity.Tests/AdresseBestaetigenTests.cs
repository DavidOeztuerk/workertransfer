using FluentAssertions;
using Noelia.Core.Identity;
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
public class ConfirmAddressTests
{
    private readonly IUserRepository _benutzer = Substitute.For<IUserRepository>();
    private readonly IVerificationTokenRepository _tokens =
        Substitute.For<IVerificationTokenRepository>();
    private readonly IOneTimeToken _einmaltoken = Substitute.For<IOneTimeToken>();
    private readonly ICompanyRepository _firmen = Substitute.For<ICompanyRepository>();
    private readonly IMembershipRepository _mitglieder = Substitute.For<IMembershipRepository>();
    private readonly IAuditTrail _protokoll = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly Guid TokenId = Guid.CreateVersion7();

    public ConfirmAddressTests() => _einmaltoken.HashOf("link").Returns("hash");

    private Task<ConfirmationResult> Confirm(string token = "link") =>
        new ConfirmAddressHandler(
            _benutzer, _tokens, _einmaltoken,
            new CreateCompany(
                _firmen, _mitglieder, _protokoll,
                new FixedCorrelation("abc-123"), TimeProvider.System),
            _protokoll, new FixedCorrelation("abc-123"), TimeProvider.System)
            .Handle(new ConfirmAddressCommand(token), CancellationToken.None);

    private void TheTokenExists(
        DateTimeOffset? consumedAt = null, DateTimeOffset? expiresAt = null) =>
        _tokens.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns(new VerificationToken(
                TokenId, Anna, "hash", TokenPurpose.EmailVerify,
                expiresAt ?? DateTimeOffset.UtcNow.AddHours(1), consumedAt));

    private User TheAccountExists(
        AccountStatus status = AccountStatus.Pending,
        string? openCompany = null,
        string email = "chef@firma.example")
    {
        var account = Accounts.Existing(Anna, email, status: status, openCompany: openCompany);
        _benutzer.FindByIdAsync(Anna, Arg.Any<CancellationToken>()).Returns(account);
        return account;
    }

    [Fact]
    public async Task A_valid_link_unlocks_the_account()
    {
        TheTokenExists();
        var account = TheAccountExists();

        var result = await Confirm();

        result.Should().BeOfType<ConfirmationResult.Confirmed>();
        account.Status.Should().Be(AccountStatus.Active);
        await _benutzer.Received().SaveAsync(account, Arg.Any<CancellationToken>());
        await _tokens.Received(1).ConsumeAsync(
            TokenId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Without the save the unlock stays in memory: the aggregate comes back
    /// detached from the repository. It costs nothing in a test — the
    /// substitute hands back the same instance — and silently loses the write.
    /// </summary>
    [Fact]
    public async Task The_activation_is_saved_expressly()
    {
        TheTokenExists();
        TheAccountExists();

        await Confirm();

        await _benutzer.Received().SaveAsync(
            Arg.Is<User>(u => u.Status == AccountStatus.Active), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_token_is_invalid()
    {
        _tokens.FindByHashAsync("hash", Arg.Any<CancellationToken>())
            .Returns((VerificationToken?)null);

        (await Confirm()).Should().BeOfType<ConfirmationResult.Invalid>();
    }

    [Fact]
    public async Task An_expired_link_gets_an_answer_of_its_own()
    {
        TheTokenExists(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));
        TheAccountExists();

        (await Confirm()).Should().BeOfType<ConfirmationResult.Expired>();
        await _benutzer.DidNotReceive().SaveAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The account is exactly as unlocked as the click wanted it. Whoever holds
    /// the token had the mail, so nothing is revealed by saying so.
    /// </summary>
    [Fact]
    public async Task Clicking_the_same_link_twice_is_no_error()
    {
        TheTokenExists(consumedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        TheAccountExists(AccountStatus.Active);

        (await Confirm()).Should().BeOfType<ConfirmationResult.Confirmed>();
    }

    /// <summary>
    /// Consumed while the account is still pending means the token was voided
    /// by a resend. The old link must not unlock anything any more.
    /// </summary>
    [Fact]
    public async Task A_link_voided_by_a_resend_no_longer_carries()
    {
        TheTokenExists(consumedAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        TheAccountExists(AccountStatus.Pending);

        (await Confirm()).Should().BeOfType<ConfirmationResult.Invalid>();
    }

    [Fact]
    public async Task The_confirmation_stands_in_the_trail()
    {
        TheTokenExists();
        TheAccountExists();

        await Confirm();

        await _protokoll.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.EmailVerified && e.Actor == Anna),
            Arg.Any<CancellationToken>());
    }

    public class WithCompanyIntent : ConfirmAddressTests
    {
        /// <summary>
        /// The domain comes from the confirmed address, never from a request —
        /// which is what makes it unforgeable (ADR-0019).
        /// </summary>
        [Fact]
        public async Task The_company_comes_into_being_only_now_and_on_the_proven_domain()
        {
            TheTokenExists();
            TheAccountExists(openCompany: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            var result = await Confirm();

            result.Should().BeOfType<ConfirmationResult.Confirmed>()
                .Which.CompanyName.Should().Be("Beispiel GmbH");
            await _firmen.Received(1).AddAsync(
                Arg.Is<Company>(f => f.Domain.Value == "firma.example"),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task Whoever_creates_their_company_becomes_its_administrator()
        {
            TheTokenExists();
            TheAccountExists(openCompany: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            await Confirm();

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
        public async Task A_claimed_domain_does_not_prevent_the_confirmation()
        {
            TheTokenExists();
            var account = TheAccountExists(openCompany: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns(Company.Restore(
                    TenantId.New(), "Andere GmbH",
                    new EmailDomain("firma.example"), TenantStatus.Active));

            var result = await Confirm();

            var confirmed = result.Should().BeOfType<ConfirmationResult.Confirmed>().Which;
            confirmed.CompanyError.Should().Be("domain_already_claimed");
            confirmed.CompanyName.Should().BeNull();
            account.Status.Should().Be(AccountStatus.Active);
        }

        /// <summary>
        /// Spent in both cases. Left standing it would sit there forever, and a
        /// second click on the same link might create a second company.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task The_intent_is_spent_afterwards_even_on_a_refusal(bool refused)
        {
            TheTokenExists();
            var account = TheAccountExists(openCompany: "Beispiel GmbH");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns(refused
                    ? Company.Restore(
                        TenantId.New(), "Andere GmbH",
                        new EmailDomain("firma.example"), TenantStatus.Active)
                    : null);

            await Confirm();

            account.PendingCompanyName.Should().BeNull();
        }

        /// <summary>
        /// A mass provider cannot be claimed. Registration already refuses it,
        /// so this is the second lock on the same door — and the one that holds
        /// if somebody ever writes the intention another way.
        /// </summary>
        [Fact]
        public async Task A_freemail_address_gets_no_company_here_either()
        {
            TheTokenExists();
            TheAccountExists(openCompany: "Beispiel GmbH", email: "chef@gmail.com");
            _firmen.FindByDomainAsync(Arg.Any<EmailDomain>(), Arg.Any<CancellationToken>())
                .Returns((Company?)null);

            var result = await Confirm();

            result.Should().BeOfType<ConfirmationResult.Confirmed>()
                .Which.CompanyError.Should().Be("public_email_domain");
            await _firmen.DidNotReceive().AddAsync(
                Arg.Any<Company>(), Arg.Any<CancellationToken>());
        }
    }
}
