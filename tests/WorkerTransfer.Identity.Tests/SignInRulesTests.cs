using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Who may sign in, and how the refusals differ.
/// </summary>
/// <remarks>
/// The distinction between the two refusals is the point: an unconfirmed
/// account is not a disabled one, and answering them alike leaves whoever
/// missed the mail in a dead end.
/// </remarks>
public class SignInRulesTests
{
    private static User Account(AccountStatus status) =>
        Accounts.Existing(SubjectId.New(), status: status);

    [Fact]
    public void An_active_account_may_sign_in()
    {
        var mayAct = () => Account(AccountStatus.Active).AssertCanSignIn();

        mayAct.Should().NotThrow();
    }

    [Fact]
    public void An_unconfirmed_account_is_not_disabled_but_unconfirmed()
    {
        var mayAct = () => Account(AccountStatus.Pending).AssertCanSignIn();

        mayAct.Should().Throw<EmailNotConfirmedException>();
    }

    [Theory]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Disabled)]
    public void Everything_else_is_shut(AccountStatus status)
    {
        var mayAct = () => Account(status).AssertCanSignIn();

        mayAct.Should().Throw<AccountDisabledException>();
    }
}
