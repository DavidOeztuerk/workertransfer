using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Whether a company still acts.</summary>
public enum TenantStatus
{
    /// <summary>Acting.</summary>
    Active,

    /// <summary>
    /// Left without an administrator, and its adverts withdrawn (ADR-0027 §7).
    /// </summary>
    /// <remarks>
    /// Not "deleted". A company is not a natural person (ADR-0017), and its
    /// adverts are still its own. The alternative — blocking an erasure until
    /// somebody else is made an administrator — would hang a personal right on
    /// an organisational question.
    /// </remarks>
    Dormant
}

/// <summary>The domain part of an address.</summary>
public sealed record EmailDomain
{
    /// <summary>
    /// Deliberately short and extendable. Completeness is not reachable; the
    /// list stops the obvious cases where somebody claims a mass provider as a
    /// company.
    /// </summary>
    public static IReadOnlySet<string> PublicProviders { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "aol.com", "freenet.de", "gmail.com", "googlemail.com",
            "gmx.at", "gmx.ch", "gmx.de", "gmx.net", "hotmail.com",
            "icloud.com", "mail.com", "me.com", "outlook.com",
            "proton.me", "protonmail.com", "t-online.de", "web.de",
            "yahoo.com", "yahoo.de", "yandex.com", "zoho.com"
        };

    /// <param name="raw">The domain, in any casing.</param>
    public EmailDomain(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        Value = raw.Trim().ToLowerInvariant();
    }

    /// <summary>The domain, lower case.</summary>
    public string Value { get; }

    /// <summary>The domain of an address.</summary>
    /// <remarks>
    /// Never from a request. The company's domain comes from the creator's
    /// <em>confirmed</em> address, which is what makes it unforgeable and why
    /// there is no unverified company state for every read path to re-check
    /// (ADR-0019).
    /// </remarks>
    public static EmailDomain FromEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var trenner = email.LastIndexOf('@');

        return trenner < 0
            ? throw new ArgumentException("Not an address.", nameof(email))
            : new EmailDomain(email[(trenner + 1)..]);
    }

    /// <summary>Whether this is a mass provider.</summary>
    public bool IsPublic => PublicProviders.Contains(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>A company, as identity: a name and a proven domain.</summary>
/// <remarks>
/// The employer profile — culture, benefits, team, careers page — belongs to
/// companies-service. What lives here is only what switching into a company
/// needs synchronously.
/// </remarks>
public sealed class Company
{
    private Company(TenantId id, string name, EmailDomain domain, TenantStatus status)
    {
        Id = id;
        Name = name;
        Domain = domain;
        Status = status;
    }

    /// <summary>Which company.</summary>
    public TenantId Id { get; }

    /// <summary>What it is called.</summary>
    public string Name { get; }

    /// <summary>The domain it was proven on.</summary>
    public EmailDomain Domain { get; }

    /// <summary>Whether it still acts.</summary>
    public TenantStatus Status { get; }

    /// <exception cref="InvalidCompanyNameException">The name is empty.</exception>
    /// <exception cref="PublicEmailDomainException">The domain is a mass provider.</exception>
    public static Company Create(string name, EmailDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var bereinigt = name?.Trim();

        if (string.IsNullOrEmpty(bereinigt))
        {
            throw new InvalidCompanyNameException();
        }

        if (domain.IsPublic)
        {
            throw new PublicEmailDomainException(domain.Value);
        }

        return new Company(TenantId.New(), bereinigt, domain, TenantStatus.Active);
    }

    /// <summary>The company as a row holds it.</summary>
    public static Company Restore(
        TenantId id, string name, EmailDomain domain, TenantStatus status) =>
        new(id, name, domain, status);
}
