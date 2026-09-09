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
        string? pendingCompanyName,
        Kontosprache sprache,
        string? givenName,
        string? familyName)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        DisplayName = displayName;
        Status = status;
        Roles = roles;
        PendingCompanyName = pendingCompanyName;
        Kontosprache = sprache;
        GivenName = LeerAlsNull(givenName);
        FamilyName = LeerAlsNull(familyName);
    }

    /// <summary>Who this is.</summary>
    public SubjectId Id { get; }

    /// <summary>The address they sign in with. Globally unique.</summary>
    public string Email { get; }

    /// <summary>The stored password entry. Never the password.</summary>
    public string PasswordHash { get; }

    /// <summary>The name they chose to be shown under.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// Bürgerlicher Vorname — für Briefkopf und Signatur, nicht für die Suche.
    /// </summary>
    /// <remarks>
    /// Getrennt vom Anzeigenamen (ADR-0038). Leer/fehlend ist die Vorgabe
    /// (Art. 25): wer sich nur umsieht, muss keinen Klarnamen hinterlegen.
    /// </remarks>
    public string? GivenName { get; private set; }

    /// <summary>Bürgerlicher Nachname. Siehe <see cref="GivenName"/>.</summary>
    public string? FamilyName { get; private set; }

    /// <summary>
    /// Was unter das Anschreiben gehört: Vor- und Nachname, sonst der Anzeigename.
    /// </summary>
    public string Klarname
    {
        get
        {
            var voll = $"{GivenName} {FamilyName}".Trim();
            return voll.Length > 0 ? voll : DisplayName;
        }
    }

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

    /// <summary>The language this person is written to in.</summary>
    /// <remarks>
    /// On the account and not on the request, because the mails that matter
    /// most go out without one — see <see cref="Users.Sprache"/>.
    /// </remarks>
    public Kontosprache Kontosprache { get; private set; }

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
    /// <param name="sprache">
    /// What to write to them in. Taken from the browser at registration, and
    /// changeable afterwards — the confirmation mail is the first thing this
    /// account ever receives, so guessing here is better than defaulting.
    /// </param>
    public static User Register(
        string email,
        string passwordHash,
        string displayName,
        string? pendingCompanyName = null,
        Kontosprache sprache = Sprachwahl.Vorgabe,
        string? givenName = null,
        string? familyName = null) =>
        new(SubjectId.New(), email, passwordHash, displayName,
            AccountStatus.Pending, ["user"], pendingCompanyName, sprache,
            givenName, familyName);

    /// <summary>The account as a row holds it.</summary>
    /// <remarks>For repositories. Everything here is already true.</remarks>
    public static User Restore(
        SubjectId id,
        string email,
        string passwordHash,
        string displayName,
        AccountStatus status,
        IReadOnlyList<string> roles,
        string? pendingCompanyName,
        Kontosprache sprache = Sprachwahl.Vorgabe,
        string? givenName = null,
        string? familyName = null) =>
        new(id, email, passwordHash, displayName, status, roles,
            pendingCompanyName, sprache, givenName, familyName);

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

    /// <summary>The person chose a language.</summary>
    /// <remarks>
    /// An explicit choice, which is why it is a method and not a setter fed by
    /// every request: the browser's header is a guess and must never quietly
    /// overwrite what somebody picked. Somebody who reads German while
    /// travelling in France should not find their mails in French afterwards.
    /// </remarks>
    /// <param name="sprache">What they picked.</param>
    public void SpracheWaehlen(Kontosprache sprache) => Kontosprache = sprache;

    /// <summary>Bürgerlichen Namen setzen oder leeren.</summary>
    /// <remarks>
    /// Leer heisst ENTFERNEN, nicht „unverändert": sonst gäbe es keinen Weg
    /// zurück zu „keiner". Der Anzeigename bleibt unberührt.
    /// </remarks>
    public void SetzeKlarname(string? givenName, string? familyName)
    {
        GivenName = LeerAlsNull(givenName);
        FamilyName = LeerAlsNull(familyName);
    }

    private static string? LeerAlsNull(string? wert)
    {
        var getrimmt = wert?.Trim();
        return string.IsNullOrEmpty(getrimmt) ? null : getrimmt;
    }

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
