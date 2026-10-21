using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Which model writes for a company — its own key, or the platform's (ADR-0051).</summary>
/// <remarks>
/// <para>The company's twin of <see cref="AccountSettings"/>, and deliberately
/// with the same two providers and the same platform-set addresses: a company
/// that types an address could send its staff's drafts anywhere, and the
/// egress boundary only knows the hosts the operator declared.</para>
///
/// <para><strong>The key lies encrypted and never comes back</strong> — the
/// interface learns that one is stored and its last four characters.</para>
/// </remarks>
public sealed class CompanyAiSettings
{
    private CompanyAiSettings(TenantId company)
    {
        Company = company;
    }

    /// <summary>Whose.</summary>
    public TenantId Company { get; }

    /// <summary>Which provider; <see cref="AiProvider.None"/> means the platform's model.</summary>
    public AiProvider Provider { get; private set; } = AiProvider.None;

    /// <summary>The provider's address — set by the platform, never by the company.</summary>
    public string Address => AiProviders.Address(Provider);

    /// <summary>Which model.</summary>
    public string Model { get; private set; } = string.Empty;

    /// <summary>The key, encrypted. Empty means: none stored.</summary>
    public string EncryptedKey { get; private set; } = string.Empty;

    /// <summary>The last four characters, for recognising it again.</summary>
    public string KeyTail { get; private set; } = string.Empty;

    /// <summary>The settings as a row holds them.</summary>
    /// <param name="company">Whose.</param>
    /// <param name="provider">Which provider.</param>
    /// <param name="model">Its model.</param>
    /// <param name="encryptedKey">The key, encrypted.</param>
    /// <param name="keyTail">The last four characters.</param>
    /// <returns>The settings.</returns>
    public static CompanyAiSettings Restore(
        TenantId company, AiProvider provider, string model, string encryptedKey, string keyTail) =>
        new(company)
        {
            Provider = provider,
            Model = model,
            EncryptedKey = encryptedKey,
            KeyTail = keyTail
        };

    /// <summary>No own access — for a company that never set one up.</summary>
    /// <param name="company">Whose.</param>
    /// <returns>Settings that leave the platform's model writing.</returns>
    public static CompanyAiSettings Default(TenantId company) => new(company);

    /// <summary>Sets the provider. The key is not touched by it.</summary>
    /// <param name="provider">Which one.</param>
    /// <param name="model">Its model; empty takes the provider's default.</param>
    public void SetAi(AiProvider provider, string model)
    {
        var named = (model ?? string.Empty).Trim();

        Provider = provider;
        Model = provider == AiProvider.None
            ? string.Empty
            : named.Length > 0 ? named : AiProviders.DefaultModel(provider);

        // No provider means no key: a secret with no purpose is the worst kind to keep.
        if (provider == AiProvider.None)
        {
            ClearKey();
        }
    }

    /// <summary>Stores a key.</summary>
    /// <param name="encrypted">The key, already encrypted.</param>
    /// <param name="suffix">Its last four characters.</param>
    public void SetKey(string encrypted, string suffix)
    {
        EncryptedKey = encrypted ?? string.Empty;
        KeyTail = suffix ?? string.Empty;
    }

    /// <summary>Removes the key.</summary>
    public void ClearKey()
    {
        EncryptedKey = string.Empty;
        KeyTail = string.Empty;
    }
}
