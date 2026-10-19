using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>The port onto the shared SecretStore.</summary>
/// <remarks>
/// One line of implementation and a type of its own all the same: the
/// application layer must not know HOW encryption happens, and a test should be
/// able to do it without a master key.
/// </remarks>
public sealed class Secrets(SecretStore store) : ISecrets
{
    /// <inheritdoc />
    public string Encrypt(string plainText) => store.Encrypt(plainText);

    /// <inheritdoc />
    public string Suffix(string plainText) => SecretStore.Suffix(plainText);

    /// <inheritdoc />
    public string? Decrypt(string? stored) => store.Decrypt(stored);
}
