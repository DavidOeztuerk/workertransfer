using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The rules the invitation itself keeps, independent of any route.
/// </summary>
/// <remarks>
/// Their own tests because the application layer also refuses an ordinary
/// member before it gets this far — and a rule guarded twice is a rule whose
/// counter-probe lies unless both guards are pinned. Breaking the outer one
/// alone leaves every journey green, which is exactly how a second guard gets
/// deleted years later as "unreachable".
/// </remarks>
public class InvitationRulesTests
{
    private static readonly TenantId Company = TenantId.New();
    private static readonly SubjectId Boss = SubjectId.New();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Invitation Issued(
        MembershipRole role = MembershipRole.Member,
        string email = "kollege@andere.example") =>
        Invitation.Issue(Company, email, role, MembershipRole.Admin, Boss, Now);

    [Fact]
    public void An_ordinary_member_cannot_invite()
    {
        var invite = () => Invitation.Issue(
            Company, "wer@anders.example", MembershipRole.Member,
            MembershipRole.Member, Boss, Now);

        invite.Should().Throw<OnlyAdminsMayInviteException>();
    }

    [Fact]
    public void An_ordinary_member_cannot_take_an_invitation_back()
    {
        var invitation = Issued();

        var withdraw = () => invitation.Withdraw(MembershipRole.Member);

        withdraw.Should().Throw<OnlyAdminsMayInviteException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    /// <summary>
    /// Tokens get forwarded, and whoever holds the link is not necessarily who
    /// was invited.
    /// </summary>
    [Fact]
    public void A_foreign_address_does_not_take_the_invitation_up()
    {
        var invitation = Issued();

        var accept = () => invitation.Accept("jemand@anders.example", Now);

        accept.Should().Throw<NotYourInvitationException>();
        invitation.Status.Should().Be(InvitationStatus.Pending);
    }

    /// <summary>
    /// The address is compared case-insensitively, because that is how the
    /// column compares it. Anything stricter would lock somebody out over the
    /// capital letter their mail client added.
    /// </summary>
    [Fact]
    public void The_spelling_of_the_address_does_not_decide()
    {
        var invitation = Issued(email: "kollege@andere.example");

        invitation.Accept("Kollege@Andere.Example", Now);

        invitation.Status.Should().Be(InvitationStatus.Accepted);
    }

    /// <summary>
    /// Seven days. Short enough that a forgotten invitation does not still open
    /// access to applicant data a year later; long enough for a holiday.
    /// </summary>
    [Fact]
    public void After_seven_days_an_invitation_no_longer_carries()
    {
        var invitation = Issued();

        var accept = () => invitation.Accept(
            "kollege@andere.example", Now + Invitation.Lifetime);

        accept.Should().Throw<InvitationExpiredException>();
    }

    [Fact]
    public void A_withdrawn_invitation_cannot_be_taken_up()
    {
        var invitation = Issued();
        invitation.Withdraw(MembershipRole.Admin);

        var accept = () => invitation.Accept("kollege@andere.example", Now);

        accept.Should().Throw<InvitationInvalidException>();
    }

    /// <summary>
    /// Withdrawing an accepted invitation would not end the membership — that
    /// is a different operation, and suggesting it here would be dangerous.
    /// </summary>
    [Fact]
    public void An_accepted_invitation_cannot_be_taken_back()
    {
        var invitation = Issued();
        invitation.Accept("kollege@andere.example", Now);

        var withdraw = () => invitation.Withdraw(MembershipRole.Admin);

        withdraw.Should().Throw<InvitationInvalidException>();
        invitation.Status.Should().Be(InvitationStatus.Accepted);
    }

    [Fact]
    public void Accepting_twice_does_not_work()
    {
        var invitation = Issued();
        invitation.Accept("kollege@andere.example", Now);

        var onceMore = () => invitation.Accept("kollege@andere.example", Now);

        onceMore.Should().Throw<InvitationInvalidException>();
    }
}
