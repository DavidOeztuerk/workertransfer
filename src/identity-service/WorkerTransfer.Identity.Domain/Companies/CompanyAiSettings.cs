using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Which model writes for a company — its own key, or the platform's (ADR-0051).</summary>
/// <remarks>
/// <para>The company's twin of <see cref="AccountSettings"/>: the same two
/// providers with platform-set addresses — and one more, the company's own
/// OpenAI-compatible server. Its address is typed, and therefore bound: https,
/// a name and not an IP address, and a host on the company's PROVEN domain
/// (ADR-0019). Anything else could send the team's drafts anywhere.</para>
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

    /// <summary>
    /// Where the model answers: the provider's address, set by the platform —
    /// or, for <see cref="AiProvider.OwnServer"/>, the company's own.
    /// </summary>
    public string Address => Provider == AiProvider.OwnServer ? OwnAddress : AiProviders.Address(Provider);

    /// <summary>The company's own server; empty for every other provider.</summary>
    public string OwnAddress { get; private set; } = string.Empty;

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
    /// <param name="ownAddress">The company's own server, or empty.</param>
    /// <returns>The settings.</returns>
    public static CompanyAiSettings Restore(
        TenantId company, AiProvider provider, string model, string encryptedKey, string keyTail,
        string ownAddress) =>
        new(company)
        {
            Provider = provider,
            Model = model,
            EncryptedKey = encryptedKey,
            KeyTail = keyTail,
            OwnAddress = provider == AiProvider.OwnServer ? ownAddress : string.Empty
        };

    /// <summary>No own access — for a company that never set one up.</summary>
    /// <param name="company">Whose.</param>
    /// <returns>Settings that leave the platform's model writing.</returns>
    public static CompanyAiSettings Default(TenantId company) => new(company);

    /// <summary>Sets a cloud provider or the platform's model. The key is not touched by it.</summary>
    /// <param name="provider">Which one — never <see cref="AiProvider.OwnServer"/>, which needs an address.</param>
    /// <param name="model">Its model; empty takes the provider's default.</param>
    /// <exception cref="ArgumentException">The own server without its address.</exception>
    public void SetAi(AiProvider provider, string model)
    {
        if (provider == AiProvider.OwnServer)
        {
            throw new ArgumentException("An own server needs its address.", nameof(provider));
        }

        var named = (model ?? string.Empty).Trim();

        Provider = provider;
        OwnAddress = string.Empty;
        Model = provider == AiProvider.None
            ? string.Empty
            : named.Length > 0 ? named : AiProviders.DefaultModel(provider);

        // No provider means no key: a secret with no purpose is the worst kind to keep.
        if (provider == AiProvider.None)
        {
            ClearKey();
        }
    }

    /// <summary>Sets the company's own OpenAI-compatible server. The key is not touched by it.</summary>
    /// <param name="address">The full chat-completions address.</param>
    /// <param name="model">The model on that server; there is no default to fall back on.</param>
    /// <param name="provenDomain">The company's proven domain (ADR-0019).</param>
    /// <exception cref="OwnServerRefused">The address or model does not hold.</exception>
    public void SetOwnServer(string address, string model, string provenDomain)
    {
        var named = (model ?? string.Empty).Trim();
        var domain = (provenDomain ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();

        if (named.Length == 0)
        {
            throw new OwnServerRefused("ai_model");
        }

        if (!Uri.TryCreate((address ?? string.Empty).Trim(), UriKind.Absolute, out var target)
            || target.Scheme != Uri.UriSchemeHttps
            || target.UserInfo.Length > 0
            // A name, never an address: the domain proof is about names, and
            // an IP literal would skip the question which company it belongs to.
            || target.HostNameType != UriHostNameType.Dns
            || domain.Length == 0
            || !(target.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)
                 || target.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OwnServerRefused("ai_base_url");
        }

        Provider = AiProvider.OwnServer;
        OwnAddress = target.AbsoluteUri;
        Model = named;
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

/// <summary>An own server that does not hold — names the field, never the value.</summary>
/// <param name="field">The wire name of the refused field.</param>
public sealed class OwnServerRefused(string field) : Exception($"invalid: {field}")
{
    /// <summary>The wire name of the refused field.</summary>
    public string Field { get; } = field;
}
