namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Verschlüsselt, was eine Person selbst hinterlegt hat.</summary>
/// <remarks>
/// Als Port und nicht als direkter Aufruf, damit die Anwendungsschicht nicht
/// weiss, WIE verschlüsselt wird — und damit ein Test es ohne Hauptschlüssel
/// tun kann. Die Umsetzung liegt in der Infrastruktur und benutzt
/// <c>ServiceDefaults.Geheimnisspeicher</c>.
/// </remarks>
public interface IGeheimnisse
{
    /// <summary>Verschlüsselt.</summary>
    /// <param name="klartext">Das Geheimnis.</param>
    /// <returns>Was in die Zeile geschrieben wird.</returns>
    string Verschluessele(string klartext);

    /// <summary>Die letzten vier Zeichen, zum Wiedererkennen.</summary>
    /// <param name="klartext">Das Geheimnis.</param>
    /// <returns>Höchstens vier Zeichen.</returns>
    string Endung(string klartext);
}
