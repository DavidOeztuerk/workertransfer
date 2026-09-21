using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Infrastructure.Sicherheit;

/// <summary>Der Port auf den gemeinsamen SecretStore.</summary>
/// <remarks>
/// Eine Zeile Umsetzung und trotzdem ein eigener Typ: die Anwendungsschicht darf
/// nicht wissen, WIE verschlüsselt wird, und ein Test soll es ohne
/// Hauptschlüssel tun können.
/// </remarks>
public sealed class Secrets(SecretStore store) : ISecrets
{
    /// <inheritdoc />
    public string Encrypt(string klartext) => store.Encrypt(klartext);

    /// <inheritdoc />
    public string Endung(string klartext) => SecretStore.Endung(klartext);

    /// <inheritdoc />
    public string? Decrypt(string? gespeichert) => store.Decrypt(gespeichert);
}
