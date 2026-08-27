using FluentAssertions;
using Girder.Core.Identity;
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
public class AnmeldeRegelnTests
{
    private static User Konto(AccountStatus status) =>
        Konten.Bestehend(SubjectId.New(), status: status);

    [Fact]
    public void Ein_aktives_Konto_darf_sich_anmelden()
    {
        var darf = () => Konto(AccountStatus.Active).AssertCanSignIn();

        darf.Should().NotThrow();
    }

    [Fact]
    public void Ein_unbestaetigtes_Konto_ist_nicht_gesperrt_sondern_unbestaetigt()
    {
        var darf = () => Konto(AccountStatus.Pending).AssertCanSignIn();

        darf.Should().Throw<EmailNotConfirmedException>();
    }

    [Theory]
    [InlineData(AccountStatus.Suspended)]
    [InlineData(AccountStatus.Disabled)]
    public void Alles_andere_ist_gesperrt(AccountStatus status)
    {
        var darf = () => Konto(status).AssertCanSignIn();

        darf.Should().Throw<AccountDisabledException>();
    }
}
