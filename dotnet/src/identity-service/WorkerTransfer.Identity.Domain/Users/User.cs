using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Where an account stands in its lifecycle.</summary>
public enum AccountStatus
{
    /// <summary>Registered, mail not yet confirmed.</summary>
    Pending,

    /// <summary>Confirmed and able to sign in.</summary>
    Active,

    /// <summary>Withheld, and expected back.</summary>
    Suspended,

    /// <summary>Ended.</summary>
    Disabled
}

/// <summary>
/// Maps <see cref="AccountStatus"/> to the values the <c>account_status</c>
/// column already holds.
/// </summary>
public static class AccountStatusNames
{
    /// <summary>The stored value for a status.</summary>
    public static string ToDatabase(AccountStatus status) => status switch
    {
        AccountStatus.Pending => "pending",
        AccountStatus.Active => "active",
        AccountStatus.Suspended => "suspended",
        AccountStatus.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown account status.")
    };

    /// <summary>The status for a stored value.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of the four.</exception>
    public static AccountStatus FromDatabase(string stored) => stored switch
    {
        "pending" => AccountStatus.Pending,
        "active" => AccountStatus.Active,
        "suspended" => AccountStatus.Suspended,
        "disabled" => AccountStatus.Disabled,
        _ => throw new ArgumentOutOfRangeException(
            nameof(stored), stored, "Unknown account status.")
    };
}

/// <summary>A person's account.</summary>
/// <remarks>
/// Carries no tenant. A tenant is a company, and a natural person has none —
/// acting for one is a second, explicit step that verifies membership first
/// (ADR-0017).
/// </remarks>
public sealed class User
{
    /// <summary>Who this is.</summary>
    public required SubjectId Id { get; init; }

    /// <summary>The address they sign in with. Globally unique.</summary>
    public required string Email { get; init; }

    /// <summary>The stored password entry. Never the password.</summary>
    public required string PasswordHash { get; init; }

    /// <summary>The name they chose to be shown under.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Where the account stands.</summary>
    public required AccountStatus Status { get; init; }

    /// <summary>The roles held, as stored on the account.</summary>
    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>
    /// Refuses a sign-in the account is not in a state for.
    /// </summary>
    /// <exception cref="EmailNotConfirmedException">The mail is unconfirmed.</exception>
    /// <exception cref="AccountDisabledException">The account is not active.</exception>
    public void AssertCanSignIn()
    {
        if (Status == AccountStatus.Pending)
        {
            throw new EmailNotConfirmedException();
        }

        if (Status != AccountStatus.Active)
        {
            throw new AccountDisabledException();
        }
    }
}
