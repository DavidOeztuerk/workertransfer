using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>The master key is missing, without which nothing can lie encrypted.</summary>
/// <param name="key">Which configuration key is missing.</param>
public sealed class MasterKeyMissing(string key)
    : Exception($"'{key}' ist nicht gesetzt. Ohne Hauptschlüssel kann kein "
                + "Geheimnis gespeichert werden — und im Klartext wird keines gespeichert.");

/// <summary>Encrypts secrets a person deposited themselves.</summary>
/// <remarks>
/// <strong>AES-GCM, one key from the environment, one random vector per
/// secret.</strong> GCM and not CBC, because it checks authenticity along the
/// way: an altered ciphertext is noticed on decryption instead of coming out as
/// nonsense. The vector stands at the front of the result — it is no secret,
/// but it must never repeat, and that is why it is rolled rather than counted.
///
/// <para>
/// <strong>The master key comes from the environment, exactly like
/// <c>JWT_SECRET</c>.</strong> That is deliberately the same mechanism and not
/// a second one: <c>make env</c> rolls it, <c>.env</c> is ignored, and without
/// it this service throws at startup naming the MISSING variable. A built-in
/// default would be the key itself, and it would then lie in git.
/// </para>
///
/// <para>
/// <strong>What this is NOT.</strong> It is not a key management system: there
/// is no rotation, no versioning of the master key and no second recipient.
/// Whoever changes the master key makes every deposited secret unreadable — the
/// interface then shows "none deposited", and the person deposits a new one.
/// That is the honest state for the first step; Infisical will later fill the
/// same variable, and only then is a rotation worth having.
/// </para>
/// </remarks>
public sealed class SecretStore
{
    /// <summary>The environment variable the master key stands in.</summary>
    public const string Variable = "WORKERTRANSFER_SECRETS_KEY";

    private readonly byte[] _hauptschluessel;

    /// <summary>Reads the master key out of the configuration.</summary>
    /// <param name="konfiguration">The service's configuration.</param>
    /// <exception cref="MasterKeyMissing">It is not set.</exception>
    public SecretStore(IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        var roh = konfiguration[Variable] ?? Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(roh))
        {
            throw new MasterKeyMissing(Variable);
        }

        // An arbitrarily long string becomes 32 bytes. SHA-256 and not the
        // bytes themselves: `make env` rolls base64, and its length hangs on
        // the source of randomness — but AES demands exactly 16, 24 or 32.
        _hauptschluessel = SHA256.HashData(Encoding.UTF8.GetBytes(roh));
    }

    /// <summary>Encrypts a secret.</summary>
    /// <param name="klartext">What is to be deposited.</param>
    /// <returns>Base64 of vector, checksum and ciphertext.</returns>
    public string Encrypt(string klartext)
    {
        ArgumentNullException.ThrowIfNull(klartext);

        var vektor = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var content = Encoding.UTF8.GetBytes(klartext);
        var geheim = new byte[content.Length];
        var pruefsumme = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_hauptschluessel, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(vektor, content, geheim, pruefsumme);

        return Convert.ToBase64String([.. vektor, .. pruefsumme, .. geheim]);
    }

    /// <summary>Decrypts a secret.</summary>
    /// <remarks>
    /// Returns <c>null</c> instead of throwing. An unreadable secret is not a
    /// program error here but a master key that was changed — and the right
    /// answer to that is "none is deposited", not a crash while drafting.
    /// </remarks>
    /// <param name="gespeichert">What stood in the row.</param>
    /// <returns>The plaintext, or <c>null</c>.</returns>
    public string? Decrypt(string? gespeichert)
    {
        if (string.IsNullOrWhiteSpace(gespeichert))
        {
            return null;
        }

        try
        {
            var alles = Convert.FromBase64String(gespeichert);
            var vektorLaenge = AesGcm.NonceByteSizes.MaxSize;
            var pruefLaenge = AesGcm.TagByteSizes.MaxSize;

            if (alles.Length <= vektorLaenge + pruefLaenge)
            {
                return null;
            }

            var vektor = alles.AsSpan(0, vektorLaenge);
            var pruefsumme = alles.AsSpan(vektorLaenge, pruefLaenge);
            var geheim = alles.AsSpan(vektorLaenge + pruefLaenge);
            var klartext = new byte[geheim.Length];

            using var aes = new AesGcm(_hauptschluessel, pruefLaenge);
            aes.Decrypt(vektor, geheim, pruefsumme, klartext);

            return Encoding.UTF8.GetString(klartext);
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The last four characters — for recognising, not for using.</summary>
    /// <param name="klartext">The secret.</param>
    /// <returns>At most four characters.</returns>
    public static string Endung(string klartext) =>
        string.IsNullOrEmpty(klartext) || klartext.Length <= 4
            ? string.Empty
            : klartext[^4..];
}
