using FluentValidation;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>
/// Der erste echte Validator dieses Systems.
/// </summary>
/// <remarks>
/// <para><strong>Die Stufe war immer da, nur leer.</strong> Girders
/// <c>AddCQRS</c> hängt <c>ValidationBehavior</c> in jede Pipeline und ruft
/// <c>AddValidatorsFromAssemblies</c> über genau diese Assembly. Das Verhalten
/// steigt sofort wieder aus, solange es keinen Validator findet — und es fand
/// keinen. Es fehlte also nie eine Verdrahtung, sondern der Inhalt.</para>
///
/// <para><strong>Warum hier und nicht am Endpunkt.</strong> Bis D2 stand diese
/// Prüfung im Endpunkt, weil die Pipeline-Stufe für tot gehalten wurde. Sie ist
/// es nicht. Hier steht sie richtiger: am Befehl, wo sie auch für einen zweiten
/// Aufrufer gilt und nicht nur für den einen Endpunkt.</para>
///
/// <para><strong>Die Meldungen nennen die Regel, nie den Wert.</strong> Das ist
/// hier keine Stilfrage: <c>ValidationBehavior</c> schreibt
/// <c>f.ErrorMessage</c> ins Protokoll, und FluentValidations Vorgabemeldungen
/// setzen Platzhalter wie <c>{PropertyValue}</c> ein. Eine Vorgabemeldung wäre
/// also ein Wert im Log. Deshalb trägt jede Regel hier eine eigene, und die
/// Antwort nach draußen nennt ohnehin nur Feldnamen
/// (<c>ProblemDetailsMiddleware</c>).</para>
///
/// <para>Keine Prüfung auf E-Mail-<em>Form</em>: ob die Adresse existiert,
/// entscheidet der Versand, und eine Formprüfung, die 422 statt 401 gibt,
/// verriete, dass die Anmeldung überhaupt so weit gekommen ist.</para>
/// </remarks>
public sealed class AnmeldenPruefung : AbstractValidator<AnmeldenBefehl>
{
    /// <summary>Setzt die zwei Regeln.</summary>
    public AnmeldenPruefung()
    {
        RuleFor(befehl => befehl.Email)
            .NotEmpty().WithMessage("email is required");

        RuleFor(befehl => befehl.Passwort)
            .NotEmpty().WithMessage("password is required");
    }
}
