using System.Net.Mail;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Ob eine Adresse zustellbar ist.</summary>
/// <remarks>
/// Geprüft wird mit demselben Parser, der später versendet. Ein eigener
/// regulärer Ausdruck wäre eine zweite Grammatik neben der von
/// <see cref="MailAddress"/>, und was hier durchginge, könnte dort scheitern —
/// dann steht die Zeile in der Datenbank und die Mail geht nie hinaus.
/// <para>
/// Geprüft wird die FORM, nie die Existenz: ob hinter der Adresse ein Postfach
/// steht, darf dieser Dienst nicht verraten (<c>/auth/register</c> antwortet für
/// bekannte und unbekannte Adressen gleich).
/// </para>
/// </remarks>
public static class Emailadresse
{
    /// <summary>Die Obergrenze aus RFC 5321: 254 Zeichen.</summary>
    public const int Hoechstlaenge = 254;

    /// <summary>Lässt sich daraus eine Mail bauen?</summary>
    /// <param name="adresse">Was jemand getippt hat.</param>
    /// <returns><c>true</c>, wenn der Versand daran nicht scheitern wird.</returns>
    public static bool IstZustellbar(string? adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse) || adresse.Length > Hoechstlaenge)
        {
            return false;
        }

        if (!MailAddress.TryCreate(adresse.Trim(), out var gebaut))
        {
            return false;
        }

        // Eine blosse Adresse, kein „Anna <a@b.de>": das stünde sonst so in der
        // Spalte, die den Menschen identifiziert.
        if (!string.Equals(gebaut.Address, adresse.Trim(), StringComparison.Ordinal)
            || gebaut.DisplayName.Length > 0)
        {
            return false;
        }

        // `a@localhost` ist syntaktisch erlaubt und im offenen Netz nicht
        // zustellbar.
        var punkt = gebaut.Host.LastIndexOf('.');

        return punkt > 0 && punkt < gebaut.Host.Length - 1;
    }
}
