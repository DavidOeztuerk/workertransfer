namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Encrypts what a person stored themselves.</summary>
/// <remarks>
/// A port and not a direct call, so the application layer does not know HOW
/// encryption happens — and so a test can do it without a master key. The
/// implementation lives in the infrastructure and uses
/// <c>ServiceDefaults.SecretStore</c>.
/// </remarks>
public interface ISecrets
{
    /// <summary>Encrypts.</summary>
    /// <param name="plainText">The secret.</param>
    /// <returns>What is written into the row.</returns>
    string Encrypt(string plainText);

    /// <summary>The last four characters, for recognising it again.</summary>
    /// <param name="plainText">The secret.</param>
    /// <returns>At most four characters.</returns>
    string Suffix(string plainText);

    /// <summary>Decrypts what <see cref="Encrypt"/> wrote.</summary>
    /// <param name="stored">What stands in the row.</param>
    /// <returns>The plaintext, or <c>null</c> when there is nothing or it is unreadable.</returns>
    string? Decrypt(string? stored);
}
