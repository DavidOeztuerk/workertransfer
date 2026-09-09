using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Infrastructure.Sicherheit;

/// <summary>Der Port auf den gemeinsamen Geheimnisspeicher.</summary>
/// <remarks>
/// Eine Zeile Umsetzung und trotzdem ein eigener Typ: die Anwendungsschicht darf
/// nicht wissen, WIE verschlüsselt wird, und ein Test soll es ohne
/// Hauptschlüssel tun können.
/// </remarks>
public sealed class Geheimnisse(Geheimnisspeicher speicher) : IGeheimnisse
{
    /// <inheritdoc />
    public string Verschluessele(string klartext) => speicher.Verschluessele(klartext);

    /// <inheritdoc />
    public string Endung(string klartext) => Geheimnisspeicher.Endung(klartext);

    /// <inheritdoc />
    public string? Entschluessele(string? gespeichert) => speicher.Entschluessele(gespeichert);
}
