using FluentValidation;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Was dastehen muss, damit ein Konto überhaupt angelegt werden kann.</summary>
/// <remarks>
/// <para><strong>Gefunden, weil es fehlte.</strong> Ein Rumpf ohne
/// <c>display_name</c> lief bis in die Datenbank und kam als <c>500</c> zurück
/// (<c>null value in column "display_name" violates not-null constraint</c>).
/// Genau die Klasse, die die Prüfstufe schließen soll — die Registrierung war
/// als einzige der vier Türen ohne Prüfer geblieben.</para>
///
/// <para><strong>Es prüft die Form, nie die Existenz.</strong> Ob die Adresse
/// bekannt ist, beantwortet dieser Endpunkt bewusst nicht: er antwortet für
/// bekannte und unbekannte gleich, sonst verriete er die Mitgliedschaft auf
/// dieser Plattform, ohne den Ledger zu fragen. Ein Prüfer sieht nur den Rumpf
/// und kann diese Zusage deshalb gar nicht brechen.</para>
///
/// <para><strong>Warum die Antwort jetzt 422 statt 400 ist.</strong> Vorher fiel
/// ein leerer Rumpf in die Fachschicht (<c>Password rejected: must not be
/// empty</c>) und kam als 400 zurück — als einziger der vier Endpunkte;
/// <c>/auth/login</c>, <c>/auth/verify-email</c> und
/// <c>/auth/resend-verification</c> antworten längst 422. Ein kaputter Rumpf ist
/// überall dieselbe Sache und heißt ab jetzt überall gleich.</para>
///
/// <para><strong>Die Adresse wird auf ihre FORM geprüft, seit sie es nicht
/// wurde.</strong> <c>NotEmpty()</c> allein liess am 10.09.2026 die Adresse
/// <c>bus</c> durch: 201, Zeile in der Datenbank, und erst der Postversand
/// scheiterte — in einem Hintergrundversand, der den Fehler protokolliert und
/// verschluckt. Zurück blieb ein Konto auf <c>pending</c>, das niemand je
/// bestätigen kann. Geprüft wird mit <see cref="Emailadresse.IstZustellbar"/>,
/// also mit demselben Parser, der später versendet — ein eigener regulärer
/// Ausdruck wäre eine zweite Grammatik, und deren Spalt ist genau die
/// Lücke.</para>
///
/// <para><strong>Kein Passwortmaß hier.</strong> Wie lang und wie
/// zusammengesetzt ein Passwort sein muss, ist eine fachliche Regel und steht,
/// wo sie hingehört. Hier steht nur, dass eines dasteht — sonst gäbe es die
/// Regel zweimal und die beiden gingen beim ersten Ändern auseinander.</para>
///
/// <para><strong>Der Firmenname bleibt ungeprüft.</strong> Er ist optional, und
/// „nicht gesetzt" ist der Normalfall: der übliche Mensch auf einem
/// Transfermarkt hat keine Firma.</para>
/// </remarks>
public sealed class RegistrierungPruefung : AbstractValidator<RegistrierenBefehl>
{
    /// <summary>Setzt die Regeln.</summary>
    public RegistrierungPruefung()
    {
        RuleFor(befehl => befehl.Email)
            .NotEmpty().WithMessage("email is required")
            // Die Meldung nennt die REGEL und nie den Wert: `ValidationBehavior`
            // protokolliert die Meldungen, und eine Adresse gehört in kein Log.
            .Must(Emailadresse.IstZustellbar).WithMessage("email is not a deliverable address")
            .OverridePropertyName("email");

        RuleFor(befehl => befehl.Passwort)
            .NotEmpty().WithMessage("password is required")
            .OverridePropertyName("password");

        RuleFor(befehl => befehl.Anzeigename)
            .NotEmpty().WithMessage("display_name is required")
            .OverridePropertyName("display_name");
    }
}
