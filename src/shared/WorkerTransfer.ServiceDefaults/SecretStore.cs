using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>The master key is missing, without which nothing can lie encrypted.</summary>
/// <param name="key">Which configuration key is missing.</param>
public sealed class MasterKeyMissing(string key)
    : Exception($"'{key}' is not set. Without a master key no secret can be "
                + "stored — and none is stored in plain text.");

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

    private readonly byte[] _masterKey;

    /// <summary>Reads the master key out of the configuration.</summary>
    /// <param name="configuration">The service's configuration.</param>
    /// <exception cref="MasterKeyMissing">It is not set.</exception>
    public SecretStore(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[Variable] ?? Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new MasterKeyMissing(Variable);
        }

        // An arbitrarily long string becomes 32 bytes. SHA-256 and not the
        // bytes themselves: `make env` rolls base64, and its length hangs on
        // the source of randomness — but AES demands exactly 16, 24 or 32.
        _masterKey = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Encrypts a secret.</summary>
    /// <param name="plainText">What is to be deposited.</param>
    /// <returns>Base64 of vector, checksum and ciphertext.</returns>
    public string Encrypt(string plainText)
    {
        ArgumentNullException.ThrowIfNull(plainText);

        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var content = Encoding.UTF8.GetBytes(plainText);
        var cipherText = new byte[content.Length];
        var checksum = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_masterKey, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, content, cipherText, checksum);

        return Convert.ToBase64String([.. nonce, .. checksum, .. cipherText]);
    }

    /// <summary>Decrypts a secret.</summary>
    /// <remarks>
    /// Returns <c>null</c> instead of throwing. An unreadable secret is not a
    /// program error here but a master key that was changed — and the right
    /// answer to that is "none is deposited", not a crash while drafting.
    /// </remarks>
    /// <param name="stored">What stood in the row.</param>
    /// <returns>The plaintext, or <c>null</c>.</returns>
    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(stored);
            var nonceLength = AesGcm.NonceByteSizes.MaxSize;
            var checksumLength = AesGcm.TagByteSizes.MaxSize;

            if (bytes.Length <= nonceLength + checksumLength)
            {
                return null;
            }

            var nonce = bytes.AsSpan(0, nonceLength);
            var checksum = bytes.AsSpan(nonceLength, checksumLength);
            var cipherText = bytes.AsSpan(nonceLength + checksumLength);
            var plainText = new byte[cipherText.Length];

            using var aes = new AesGcm(_masterKey, checksumLength);
            aes.Decrypt(nonce, cipherText, checksum, plainText);

            return Encoding.UTF8.GetString(plainText);
        }
        catch (Exception error) when (error is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>The last four characters — for recognising, not for using.</summary>
    /// <param name="plainText">The secret.</param>
    /// <returns>At most four characters.</returns>
    public static string Suffix(string plainText) =>
        string.IsNullOrEmpty(plainText) || plainText.Length <= 4
            ? string.Empty
            : plainText[^4..];
}
