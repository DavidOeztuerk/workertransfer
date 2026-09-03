using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace WorkerTransfer.ServiceDefaults;

/// <summary>Es fehlt der Hauptschlüssel, ohne den nichts verschlüsselt liegen kann.</summary>
/// <param name="schluessel">Welcher Konfigurationsschlüssel fehlt.</param>
public sealed class HauptschluesselFehlt(string schluessel)
    : Exception($"'{schluessel}' ist nicht gesetzt. Ohne Hauptschlüssel kann kein "
                + "Geheimnis gespeichert werden — und im Klartext wird keines gespeichert.");

/// <summary>Verschlüsselt Geheimnisse, die eine Person selbst hinterlegt hat.</summary>
/// <remarks>
/// <strong>AES-GCM, ein Schlüssel aus der Umgebung, ein Zufallsvektor je
/// Geheimnis.</strong> GCM und nicht CBC, weil es die Echtheit mitprüft: ein
/// veränderter Geheimtext fällt beim Entschlüsseln auf, statt als Unsinn
/// herauszukommen. Der Vektor steht vorn im Ergebnis — er ist kein Geheimnis,
/// aber er darf sich nie wiederholen, und deshalb wird er gewürfelt und nicht
/// gezählt.
///
/// <para>
/// <strong>Der Hauptschlüssel kommt aus der Umgebung, genau wie
/// <c>JWT_SECRET</c>.</strong> Das ist bewusst dieselbe Mechanik und nicht eine
/// zweite: <c>make env</c> würfelt ihn, <c>.env</c> ist ignoriert, und ohne ihn
/// wirft dieser Dienst beim Start mit dem NAMEN der fehlenden Variablen. Eine
/// eingebaute Vorgabe wäre der Schlüssel selbst, und der läge dann in git.
/// </para>
///
/// <para>
/// <strong>Was das NICHT ist.</strong> Es ist kein Schlüsselverwaltungssystem:
/// es gibt keine Rotation, keine Versionierung des Hauptschlüssels und keinen
/// zweiten Empfänger. Wer den Hauptschlüssel wechselt, macht jedes hinterlegte
/// Geheimnis unlesbar — die Oberfläche zeigt dann „keiner hinterlegt", und die
/// Person legt einen neuen an. Das ist der ehrliche Zustand für den ersten
/// Schritt; Infisical füllt später dieselbe Variable, und erst dann lohnt eine
/// Rotation.
/// </para>
/// </remarks>
public sealed class Geheimnisspeicher
{
    /// <summary>Die Umgebungsvariable, in der der Hauptschlüssel steht.</summary>
    public const string Variable = "WORKERTRANSFER_SECRETS_KEY";

    private readonly byte[] _hauptschluessel;

    /// <summary>Liest den Hauptschlüssel aus der Konfiguration.</summary>
    /// <param name="konfiguration">Die Konfiguration des Dienstes.</param>
    /// <exception cref="HauptschluesselFehlt">Er ist nicht gesetzt.</exception>
    public Geheimnisspeicher(IConfiguration konfiguration)
    {
        ArgumentNullException.ThrowIfNull(konfiguration);

        var roh = konfiguration[Variable] ?? Environment.GetEnvironmentVariable(Variable);

        if (string.IsNullOrWhiteSpace(roh))
        {
            throw new HauptschluesselFehlt(Variable);
        }

        // Aus einer beliebig langen Zeichenkette werden 32 Byte. SHA-256 und
        // nicht die Bytes selbst: `make env` würfelt Base64, und dessen Länge
        // hängt an der Zufallsquelle — AES verlangt aber genau 16, 24 oder 32.
        _hauptschluessel = SHA256.HashData(Encoding.UTF8.GetBytes(roh));
    }

    /// <summary>Verschlüsselt ein Geheimnis.</summary>
    /// <param name="klartext">Was hinterlegt werden soll.</param>
    /// <returns>Base64 aus Vektor, Prüfsumme und Geheimtext.</returns>
    public string Verschluessele(string klartext)
    {
        ArgumentNullException.ThrowIfNull(klartext);

        var vektor = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var inhalt = Encoding.UTF8.GetBytes(klartext);
        var geheim = new byte[inhalt.Length];
        var pruefsumme = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(_hauptschluessel, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(vektor, inhalt, geheim, pruefsumme);

        return Convert.ToBase64String([.. vektor, .. pruefsumme, .. geheim]);
    }

    /// <summary>Entschlüsselt ein Geheimnis.</summary>
    /// <remarks>
    /// Gibt <c>null</c> zurück, statt zu werfen. Ein unlesbares Geheimnis ist
    /// hier kein Programmfehler, sondern ein gewechselter Hauptschlüssel — und
    /// die richtige Antwort darauf ist „es ist keiner hinterlegt", nicht ein
    /// Absturz beim Entwerfen.
    /// </remarks>
    /// <param name="gespeichert">Was in der Zeile stand.</param>
    /// <returns>Der Klartext, oder <c>null</c>.</returns>
    public string? Entschluessele(string? gespeichert)
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
        catch (Exception fehler) when (fehler is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Die letzten vier Zeichen — zum Wiedererkennen, nicht zum Benutzen.</summary>
    /// <param name="klartext">Das Geheimnis.</param>
    /// <returns>Höchstens vier Zeichen.</returns>
    public static string Endung(string klartext) =>
        string.IsNullOrEmpty(klartext) || klartext.Length <= 4
            ? string.Empty
            : klartext[^4..];
}
