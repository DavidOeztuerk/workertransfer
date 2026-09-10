using System.Net.Mail;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Ob eine Adresse überhaupt zustellbar ist.</summary>
/// <remarks>
/// <strong>Gefunden, weil es fehlte, und der Fund ist die Begründung.</strong>
/// Am 10.09.2026 wurde ein Konto mit der Adresse <c>bus</c> angelegt:
/// <c>POST /auth/register</c> antwortete <c>201</c>, die Zeile stand in der
/// Datenbank, und erst der Postversand scheiterte — mit
/// <c>System.FormatException: The specified string is not in the form required
/// for an e-mail address</c>, tief in einem Hintergrundversand, wo der Fehler
/// protokolliert und verschluckt wurde. Zurück blieb ein Konto auf
/// <c>pending</c>, das niemand je bestätigen kann, während die Oberfläche
/// „wir haben dir eine E-Mail geschickt" versprach.
/// <para>
/// <strong>Geprüft wird mit genau dem Parser, der später auch versendet.</strong>
/// Das ist der ganze Punkt. Ein eigener regulärer Ausdruck wäre eine ZWEITE
/// Grammatik neben der von <see cref="MailAddress"/>, und der Spalt zwischen
/// beiden ist genau die Lücke, durch die <c>bus</c> gekommen ist: was hier
/// durchgeht, muss dort baubar sein, sonst prüfen wir das eine und verschicken
/// das andere.
/// </para>
/// <para>
/// <strong>Es prüft die FORM, nie die Existenz.</strong> Ob hinter der Adresse
/// ein Postfach steht, weiss nur die Zustellung, und danach zu fragen wäre auf
/// dieser Plattform ohnehin verboten: <c>/auth/register</c> antwortet für
/// bekannte und unbekannte Adressen gleich. Diese Prüfung sieht nur die
/// Zeichenkette und kann diese Zusage deshalb gar nicht brechen.
/// </para>
/// </remarks>
public static class Emailadresse
{
    /// <summary>
    /// Die Obergrenze aus RFC 5321 §4.5.3.1.3: 254 Zeichen für den ganzen Pfad.
    /// </summary>
    /// <remarks>
    /// Eine Grenze und keine Vorliebe. Ohne sie nimmt die Spalte an, was ein
    /// Aufrufer in einer Schleife baut — und <see cref="MailAddress"/> hätte
    /// gegen eine 10.000 Zeichen lange, formal gültige Adresse nichts
    /// einzuwenden.
    /// </remarks>
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

        // `TryCreate` ist grosszügig, wo RFC 5321 es ist — und es nimmt Formen
        // an, die als ABSENDERZEILE gültig sind, aber kein Postfach benennen:
        // „Anna <a@b.de>" etwa, oder eine Adresse ohne Punkt in der Domain.
        // Deshalb steht darunter noch, was ein Postfach ausmacht.
        if (!MailAddress.TryCreate(adresse.Trim(), out var gebaut))
        {
            return false;
        }

        // Der Parser hat die Adresse als BLOSSE Adresse zu lesen — nicht als
        // Anzeigename plus Adresse. Sonst käme „Anna <a@b.de>" durch und stünde
        // so in der Spalte, die den Menschen identifiziert.
        if (!string.Equals(gebaut.Address, adresse.Trim(), StringComparison.Ordinal)
            || gebaut.DisplayName.Length > 0)
        {
            return false;
        }

        // Eine Domain ohne Punkt ist syntaktisch erlaubt (`a@localhost`) und
        // im offenen Netz nicht zustellbar. Wer eine solche Adresse einträgt,
        // bekäme dieselbe stumme Sackgasse wie bei `bus`.
        var punkt = gebaut.Host.LastIndexOf('.');

        return punkt > 0 && punkt < gebaut.Host.Length - 1;
    }
}
