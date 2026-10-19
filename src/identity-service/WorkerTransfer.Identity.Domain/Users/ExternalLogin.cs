using Noelia.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Who signs a person in besides their password (ADR-0053).</summary>
public enum ExternalProvider
{
    /// <summary>Google.</summary>
    Google,

    /// <summary>Microsoft (Entra ID, personal and work accounts).</summary>
    Microsoft,

    /// <summary>Sign in with Apple.</summary>
    Apple
}

/// <summary>The words providers travel under, and what each one vouches for.</summary>
public static class ExternalProviders
{
    /// <summary>Every provider, in the order the page shows them.</summary>
    public static IReadOnlyList<ExternalProvider> All { get; } =
        [ExternalProvider.Google, ExternalProvider.Microsoft, ExternalProvider.Apple];

    /// <summary>The word for a provider.</summary>
    public static string Word(ExternalProvider provider) => provider switch
    {
        ExternalProvider.Google => "google",
        ExternalProvider.Microsoft => "microsoft",
        ExternalProvider.Apple => "apple",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    /// <summary>The provider for a word, or <c>null</c>.</summary>
    public static ExternalProvider? Read(string? word) => word switch
    {
        "google" => ExternalProvider.Google,
        "microsoft" => ExternalProvider.Microsoft,
        "apple" => ExternalProvider.Apple,
        _ => null
    };

    /// <summary>Whether a provider's <c>email_verified</c> can be believed at all.</summary>
    /// <remarks>
    /// <strong>Microsoft cannot</strong> — measured 18.10.2026: its discovery
    /// document lists no <c>email_verified</c>, and a tenant enters whatever
    /// address it likes into <c>email</c>. Believing it would let a tenant sign
    /// in as anybody whose address it types (ADR-0053).
    /// </remarks>
    public static bool VouchesForEmail(ExternalProvider provider) => provider != ExternalProvider.Microsoft;
}

/// <summary>What a provider said about the person, after the token was checked.</summary>
/// <param name="Subject">The provider's stable id for the person — the only thing linked by.</param>
/// <param name="Email">The address the provider names, or <c>null</c>.</param>
/// <param name="EmailVerified">Whether the provider says it verified the address.</param>
public sealed record ExternalIdentity(
    ExternalProvider Provider,
    string Subject,
    string? Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Name)
{
    /// <summary>Whether the address may be taken as proven — the provider vouches, and says it did.</summary>
    public bool EmailProven => EmailVerified && ExternalProviders.VouchesForEmail(Provider) && !string.IsNullOrWhiteSpace(Email);

    /// <summary>The name to be shown under: what the provider gave, else the address's local part.</summary>
    public string DisplayName
    {
        get
        {
            var full = string.IsNullOrWhiteSpace(Name) ? $"{GivenName} {FamilyName}".Trim() : Name.Trim();

            if (full.Length > 0)
            {
                return full.Length <= 100 ? full : full[..100];
            }

            var at = (Email ?? string.Empty).IndexOf('@', StringComparison.Ordinal);
            return at > 0 ? Email![..at] : "—";
        }
    }
}

/// <summary>A provider's id for a person, bound to their account (ADR-0053).</summary>
/// <remarks>
/// Linked by <see cref="Subject"/>, never by the address: an address a provider
/// names is not proof that the account behind it belongs to the same person.
/// No token is kept — it is needed for one exchange and then forgotten.
/// </remarks>
public sealed record ExternalLogin(ExternalProvider Provider, string Subject, SubjectId User, DateTimeOffset CreatedAt);

/// <summary>Where the links live.</summary>
public interface IExternalLoginStore
{
    /// <summary>The link for a provider's id, or <c>null</c>.</summary>
    Task<ExternalLogin?> FindAsync(ExternalProvider provider, string subject, CancellationToken cancellationToken = default);

    /// <summary>This account's links.</summary>
    Task<IReadOnlyList<ExternalLogin>> ForUserAsync(SubjectId user, CancellationToken cancellationToken = default);

    /// <summary>Adds one.</summary>
    Task AddAsync(ExternalLogin login, CancellationToken cancellationToken = default);

    /// <summary>Removes this account's link to a provider; says whether there was one.</summary>
    Task<bool> DeleteAsync(SubjectId user, ExternalProvider provider, CancellationToken cancellationToken = default);
}
