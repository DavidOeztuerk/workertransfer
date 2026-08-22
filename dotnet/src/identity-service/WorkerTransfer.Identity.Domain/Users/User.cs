using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Where an account stands in its lifecycle.</summary>
/// <remarks>
/// Every member maps to one label of the <c>account_status</c> enum in the
/// database, snake_case of the member name. <c>SpaltenetikettenTests</c> pins
/// all four against the real column, because the rule that produces them is
/// Npgsql's and not ours.
/// </remarks>
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

/// <summary>A person's account.</summary>
/// <remarks>
/// Carries no tenant. A tenant is a company, and a natural person has none —
/// acting for one is a second, explicit step that verifies membership first
/// (ADR-0017).
/// <para>
/// Built only through <see cref="Register"/> or <see cref="Restore"/>, never by
/// an object initialiser. An aggregate whose state can be set from outside is
/// exactly what the row-to-aggregate mapping exists to prevent: the two
/// entrances mean something different, and one of them is a new person.
/// </para>
/// </remarks>
public sealed class User
{
    private User(
        SubjectId id,
        string email,
        string passwordHash,
        string displayName,
        AccountStatus status,
        IReadOnlyList<string> roles,
        string? pendingCompanyName)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Status = status;
        Roles = roles;
        PendingCompanyName = pendingCompanyName;
    }

    /// <summary>Who this is.</summary>
    public SubjectId Id { get; }

    /// <summary>The address they sign in with. Globally unique.</summary>
    public string Email { get; }

    /// <summary>The stored password entry. Never the password.</summary>
    public string PasswordHash { get; }

    /// <summary>The name they chose to be shown under.</summary>
    public string DisplayName { get; }

    /// <summary>Where the account stands.</summary>
    public AccountStatus Status { get; private set; }

    /// <summary>The roles held, as stored on the account.</summary>
    public IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// The company this person meant to create when they registered.
    /// </summary>
    /// <remarks>
    /// An intention, not a company. It is redeemed when the address is
    /// confirmed and not a moment earlier: the domain a company is claimed on
    /// comes from a <em>proven</em> address, so it cannot be forged (ADR-0019).
    /// <c>null</c> means a person registered, which is the ordinary case.
    /// </remarks>
    public string? PendingCompanyName { get; private set; }

    /// <summary>A new account, unconfirmed.</summary>
    /// <remarks>
    /// Registering is an act of a natural person (ADR-0017); membership in a
    /// company is granted afterwards and lives in its own relation.
    /// </remarks>
    /// <param name="email">The address, already normalised.</param>
    /// <param name="passwordHash">What the hasher produced. Never the password.</param>
    /// <param name="displayName">The name to be shown under.</param>
    /// <param name="pendingCompanyName">
    /// A company to create once the address is confirmed, or <c>null</c>.
    /// </param>
    public static User Register(
        string email,
        string passwordHash,
        string displayName,
        string? pendingCompanyName = null) =>
        new(SubjectId.New(), email, passwordHash, displayName,
            AccountStatus.Pending, ["user"], pendingCompanyName);

    /// <summary>The account as a row holds it.</summary>
    /// <remarks>For repositories. Everything here is already true.</remarks>
    public static User Restore(
        SubjectId id,
        string email,
        string passwordHash,
        string displayName,
        AccountStatus status,
        IReadOnlyList<string> roles,
        string? pendingCompanyName) =>
        new(id, email, passwordHash, displayName, status, roles, pendingCompanyName);

    /// <summary>The address was confirmed.</summary>
    /// <remarks>
    /// Only from <see cref="AccountStatus.Pending"/>. Confirming an account
    /// that was suspended or ended would let an old link in a mailbox undo a
    /// decision somebody made about it.
    /// </remarks>
    public void Activate()
    {
        if (Status == AccountStatus.Pending)
        {
            Status = AccountStatus.Active;
        }
    }

    /// <summary>The remembered intention is spent — redeemed or refused.</summary>
    /// <remarks>
    /// Called in <em>both</em> cases, and that is the point: the confirmation
    /// token is consumed either way, so there is no second attempt. Left
    /// standing, the intention would sit there forever, and a second click on
    /// the same link might create a second company.
    /// <para>
    /// If the domain was already claimed the right way is a different one
    /// anyway: whoever holds a confirmed address on that domain has colleagues
    /// there, and colleagues can invite.
    /// </para>
    /// </remarks>
    public void CompanyIntentSpent() => PendingCompanyName = null;

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
