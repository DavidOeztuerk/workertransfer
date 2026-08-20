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
    private static User Konto(AccountStatus status) => new()
    {
        Id = SubjectId.New(),
        Email = "anna@example.com",
        PasswordHash = "$2b$12$abcdefghijklmnopqrstuv",
        DisplayName = "Anna",
        Status = status,
        Roles = ["user"]
    };

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

    /// <summary>
    /// The values are the ones already in the <c>account_status</c> column, so
    /// the two systems read the same rows.
    /// </summary>
    [Theory]
    [InlineData(AccountStatus.Pending, "pending")]
    [InlineData(AccountStatus.Active, "active")]
    [InlineData(AccountStatus.Suspended, "suspended")]
    [InlineData(AccountStatus.Disabled, "disabled")]
    public void Die_Zustaende_heissen_wie_in_der_Datenbank(AccountStatus status, string gespeichert)
    {
        AccountStatusNames.ToDatabase(status).Should().Be(gespeichert);
        AccountStatusNames.FromDatabase(gespeichert).Should().Be(status);
    }

    [Fact]
    public void Ein_unbekannter_Zustand_wird_nicht_geraten()
    {
        var lesen = () => AccountStatusNames.FromDatabase("halbwegs-aktiv");

        lesen.Should().Throw<ArgumentOutOfRangeException>();
    }
}
