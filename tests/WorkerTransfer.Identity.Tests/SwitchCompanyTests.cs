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

/// <summary>
/// Acting for a company: the caller names it, the membership decides.
/// </summary>
public class SwitchCompanyTests
{
    private readonly IMembershipRepository _members = Substitute.For<IMembershipRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly ISessionCapacity _capacity = Substitute.For<ISessionCapacity>();
    private readonly IAccessTokenIssuer _token = Substitute.For<IAccessTokenIssuer>();
    private readonly IAuditTrail _auditTrail = Substitute.For<IAuditTrail>();

    private static readonly SubjectId Anna = SubjectId.New();
    private static readonly TenantId Company = TenantId.New();
    private static readonly SessionId Session = SessionId.New();

    private Task<CompanySwitchResult> Switch() =>
        new SwitchCompanyHandler(
            _members, _users, _sessions, _capacity, _token, _auditTrail,
            new FixedCorrelation("abc-123"), TimeProvider.System)
            .Handle(new SwitchCompanyCommand(Anna, Company), CancellationToken.None);

    private void TheAccountExists(AccountStatus status = AccountStatus.Active) =>
        _users.FindByIdAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(Accounts.Existing(Anna, "anna@example.com", "$2b$12$x", status));

    private void ASessionComesIntoBeing()
    {
        _sessions.StartAsync(Anna, Arg.Any<CancellationToken>())
            .Returns(new StartedSession(Session, "refresh", DateTimeOffset.UtcNow.AddDays(1)));
        _token.IssueAsync(
                Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
                Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns("access");
    }

    [Fact]
    public async Task A_member_gets_a_pair_that_acts_for_the_company()
    {
        _members.IsMemberAsync(Anna, Company, Arg.Any<CancellationToken>()).Returns(true);
        TheAccountExists();
        ASessionComesIntoBeing();

        var result = await Switch();

        result.Should().BeOfType<CompanySwitchResult.Switched>();
        await _token.Received().IssueAsync(
            Anna, "anna@example.com",
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(Company))),
            Session, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The whole point: the tenant that reaches a token was never taken from
    /// the request, it was derived from a checked membership.
    /// </summary>
    [Fact]
    public async Task Without_a_membership_no_token_is_issued()
    {
        _members.IsMemberAsync(Anna, Company, Arg.Any<CancellationToken>()).Returns(false);

        var result = await Switch();

        result.Should().BeOfType<CompanySwitchResult.NoMember>();
        await _token.DidNotReceive().IssueAsync(
            Arg.Any<SubjectId>(), Arg.Any<string>(), Arg.Any<Capacity>(),
            Arg.Any<SessionId>(), Arg.Any<CancellationToken>());
        await _sessions.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The company the caller asked for is the point of the record — especially
    /// because it was refused.
    /// </summary>
    [Fact]
    public async Task A_refused_request_names_the_company_in_the_trail()
    {
        _members.IsMemberAsync(Anna, Company, Arg.Any<CancellationToken>()).Returns(false);

        await Switch();

        await _auditTrail.Received(1).AppendAsync(
            Arg.Is<AuditEvent>(e => e.Action == AuditAction.TenantSwitchDenied
                                    && e.Actor == Anna
                                    && e.Tenant == Company),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A fresh sign-in, not an edit of the old one: the personal refresh token
    /// keeps working, and the company-bound one can be ended on its own.
    /// </summary>
    [Fact]
    public async Task The_switch_begins_a_session_of_its_own_and_remembers_the_company()
    {
        _members.IsMemberAsync(Anna, Company, Arg.Any<CancellationToken>()).Returns(true);
        TheAccountExists();
        ASessionComesIntoBeing();

        await Switch();

        await _sessions.Received(1).StartAsync(Anna, Arg.Any<CancellationToken>());
        await _capacity.Received(1).RememberAsync(
            Session,
            Arg.Is<Capacity>(c => c.Equals(new Capacity.ForCompany(Company))),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Pending)]
    public async Task An_account_that_may_not_act_does_not_switch_either(AccountStatus status)
    {
        _members.IsMemberAsync(Anna, Company, Arg.Any<CancellationToken>()).Returns(true);
        TheAccountExists(status);

        var result = await Switch();

        result.Should().BeOfType<CompanySwitchResult.Refused>();
        await _sessions.DidNotReceive().StartAsync(
            Arg.Any<SubjectId>(), Arg.Any<CancellationToken>());
    }
}
