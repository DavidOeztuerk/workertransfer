using System.Security.Cryptography;
using System.Text;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>Thirty-two random bytes, stored as their SHA-256.</summary>
/// <remarks>
/// No salt, and that is deliberate: the plaintext is already high-entropy, so
/// there is nothing to slow a guesser down that the length does not already do
/// — and a salt would make looking a token up by its hash impossible, which is
/// the only way it is ever found.
/// </remarks>
public sealed class Sha256OneTimeToken : IOneTimeToken
{
    /// <summary>32 bytes url-safe ≈ 43 characters. Guessing is not a way in.</summary>
    private const int Bytes = 32;

    /// <inheritdoc />
    public (string PlainText, string Hash) Create()
    {
        var plainText = Base64UrlEncode(RandomNumberGenerator.GetBytes(Bytes));

        return (plainText, HashOf(plainText));
    }

    /// <inheritdoc />
    public string HashOf(string plainText) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainText)));

    /// <summary>
    /// The same alphabet Python's <c>secrets.token_urlsafe</c> produces, so a
    /// link issued by either service survives the other reading it.
    /// </summary>
    private static string Base64UrlEncode(byte[] raw) =>
        Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
