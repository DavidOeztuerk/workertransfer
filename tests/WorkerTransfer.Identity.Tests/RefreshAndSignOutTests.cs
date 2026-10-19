using FluentAssertions;
using Noelia.Core.Identity;
using NSubstitute;
using WorkerTransfer.Identity.Application.SignIn;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Carrying a sign-in forward, and ending it.</summary>
public class RefreshAndSignOutTests
{
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();
    private readonly IMembershipRepository _members = Substitute.For<IMembershipRepository>();
    private readonly ISessionCapacity _capacity = Substitute.For<ISessionCapacity>();
    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly SessionId Session = SessionId.New();

    private RefreshHandler Refresh() => new(
        _sessions, _users, _members, _capacity, _token, _auditTrail,
        new FixedCorrelation("abc-123"), TimeProvider.System);

    private Task<RefreshResult> Refresh(string token) =>
        Refresh().Handle(new RefreshCommand(token), CancellationToken.None);

    private void ActsAsAPrivatePerson() =>
        _capacity.RecallAsync(Session, Arg.Any<CancellationToken>())
            .Returns(Capacity.AsSelf.Instance);

    private void ActsFor(TenantId company) =>
        _capacity.RecallAsync(Session, Arg.Any<CancellationToken>())
            .Returns(new Capacity.ForCompany(company));

    private void TheSessionStillCarries() =>
        _sessions.RenewAsync("old", Arg.Any<CancellationToken>())
            .Returns(new RenewedSession(Session, Anna, "new", DateTimeOffset.UtcNow.AddDays(1)));

    private void TheAccountExists() =>
        _users.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Accounts.Existing(Anna, "anna@example.com", "$2b$12$x", AccountStatus.Active));

    [Fact]
    public async Task A_valid_refresh_token_gives_a_fresh_pair()
    {
        TheSessionStillCarries();
        ActsAsAPrivatePerson();
        TheAccountExists();
        _token.IssueAsync(Anna, "anna@example.com", Arg.Any<Capacity>(), Session,
            Arg.Any<CancellationToken>()).Returns("fresh");

        var result = await Refresh("old");

        var refreshed = result.Should().BeOfType<RefreshResult.Refreshed>().Which;
        refreshed.AccessToken.Should().Be("fresh");
        refreshed.RefreshToken.Should().Be("new");
    }

    /// <summary>
    /// The sign-in keeps its identity across every rotation, so a revocation
    /// can still name the device it belongs to.
    /// </summary>
    [Fact]
    public async Task The_session_stays_the_same()
    {
        TheSessionStillCarries();
        ActsAsAPrivatePerson();
        TheAccountExists();

        await Refresh("old");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(), Arg.Any<Capacity>(), Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_spent_or_unknown_token_is_refused()
    {
        _sessions.RenewAsync("dead", Arg.Any<CancellationToken>()).Returns((RenewedSession?)null);

        var result = await Refresh("dead");

        result.Should().BeOfType<RefreshResult.Rejected>();
    }

    [Fact]
    public async Task Without_an_account_no_token_is_issued()
    {
        TheSessionStillCarries();
        ActsAsAPrivatePerson();
        _users.FindByIdAsync(Anna, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await Refresh("old");

        result.Should().BeOfType<RefreshResult.Rejected>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Signing in gives a person token; so does refreshing. Acting for a
    /// company is a step of its own — see <c>ISessionCapacity</c>.
    /// </summary>
    [Fact]
    public async Task A_refresh_makes_nobody_a_company()
    {
        TheSessionStillCarries();
        ActsAsAPrivatePerson();
        TheAccountExists();

        await Refresh("old");

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
    public async Task A_lapsed_membership_falls_away_at_the_refresh()
    {
        var company = TenantId.New();
        TheSessionStillCarries();
        TheAccountExists();
        ActsFor(company);
        _members.IsMemberAsync(Anna, company, Arg.Any<CancellationToken>()).Returns(false);

        await Refresh("old");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(), Arg.Is<Capacity>(c => c is Capacity.AsSelf),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
        await _auditTrail.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.TenantSwitchDenied
                                    && e.Tenant == company
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
    public async Task A_lapsed_membership_does_not_end_the_session()
    {
        var company = TenantId.New();
        TheSessionStillCarries();
        TheAccountExists();
        ActsFor(company);
        _members.IsMemberAsync(Anna, company, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Refresh("old");

        result.Should().BeOfType<RefreshResult.Refreshed>();
        await _sessions.DidNotReceive().EndAsync(
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_existing_membership_survives_the_refresh()
    {
        var company = TenantId.New();
        TheSessionStillCarries();
        TheAccountExists();
        ActsFor(company);
        _members.IsMemberAsync(Anna, company, Arg.Any<CancellationToken>()).Returns(true);

        await Refresh("old");

        await _token.Received().IssueAsync(
            Anna, Arg.Any<string>(),
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(company))),
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
    public async Task An_account_that_may_not_sign_in_is_not_refreshed_either(
        AccountStatus status)
    {
        TheSessionStillCarries();
        ActsAsAPrivatePerson();
        _users.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Accounts.Existing(Anna, "anna@example.com", "$2b$12$x", status));

        var result = await Refresh("old");

        result.Should().BeOfType<RefreshResult.Rejected>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
    }

    public class SignOut
    {
        private readonly ISessionService _sessions = Substitute.For<ISessionService>();
        private readonly IAccessRevocation _revocation = Substitute.For<IAccessRevocation>();
        private readonly ISessionCapacity _capacity = Substitute.For<ISessionCapacity>();
        private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();

        private static readonly SubjectId Anna = SubjectId.New();
        private static readonly SessionId Session = SessionId.New();

        private SignOutHandler Handler() => new(
            _sessions, _revocation, _capacity, _auditTrail, new FixedCorrelation(null), TimeProvider.System);

        private Task SignOut_(string? token) =>
            Handler().Handle(new SignOutCommand(token), CancellationToken.None);

        [Fact]
        public async Task Ends_the_session_the_presented_token_belongs_to()
        {
            _sessions.EndByRefreshTokenAsync("mine", Arg.Any<CancellationToken>())
                .Returns(new EndedSession(Session, Anna));

            await SignOut_("mine");

            await _sessions.Received().EndByRefreshTokenAsync(
                "mine", Arg.Any<CancellationToken>());
            await _auditTrail.Received(1).AppendAsync(
                Arg.Is<AuditEvent>(e => e.Action == AuditAction.TokenRevoke && e.Actor == Anna),
                Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// The sign-in is over, so what it acted as is no longer an answer to
        /// anything — and a row naming a company on a session that no longer
        /// exists is worse than none.
        /// </summary>
        [Fact]
        public async Task Forgets_what_the_session_was_acting_for()
        {
            _sessions.EndByRefreshTokenAsync("mine", Arg.Any<CancellationToken>())
                .Returns(new EndedSession(Session, Anna));

            await SignOut_("mine");

            await _capacity.Received(1).ForgetAsync(Session, Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task Without_a_token_nothing_happens_and_nobody_learns_of_it(string? none)
        {
            var signOut = () => SignOut_(none);

            await signOut.Should().NotThrowAsync();
            await _sessions.DidNotReceive().EndByRefreshTokenAsync(
                Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        /// <summary>
        /// A token that named no sign-in leaves nothing to record. Writing an
        /// entry anyway would put a sign-out in the trail that never happened.
        /// </summary>
        [Fact]
        public async Task A_dead_token_produces_no_trail_entry()
        {
            _sessions.EndByRefreshTokenAsync("dead", Arg.Any<CancellationToken>())
                .Returns((EndedSession?)null);

            await SignOut_("dead");

            await _auditTrail.DidNotReceive().AppendAsync(
                Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
        }
    }
}
