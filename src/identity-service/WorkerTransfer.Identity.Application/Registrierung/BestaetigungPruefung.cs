using FluentValidation;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Ein Bestätigungstoken muss dasein, bevor jemand ihn nachschlägt.</summary>
/// <remarks>
/// <para>Vorher stand die Prüfung im Endpunkt, weil die Pipeline-Stufe für tot
/// gehalten wurde. Sie ist es nicht — sie war leer. Am Befehl gilt sie auch für
/// einen zweiten Aufrufer.</para>
///
/// <para><strong>Leer ist nicht ungültig.</strong> Ein fehlender Token ist eine
/// kaputte Anfrage (422); ein Token, den es nicht gibt, ist eine Antwort und
/// gehört in den Handler. Die beiden zu vermischen hieße, aus „du hast nichts
/// geschickt" und „dieser Link ist verbraucht" dieselbe Auskunft zu machen.</para>
/// </remarks>
public sealed class BestaetigungPruefung : AbstractValidator<AdresseBestaetigenBefehl>
{
    /// <summary>Setzt die eine Regel.</summary>
    public BestaetigungPruefung() =>
        RuleFor(befehl => befehl.Token)
            .NotEmpty().WithMessage("token is required")
            .OverridePropertyName("token");
}

/// <summary>Eine Adresse muss zustellbar sein, damit die Mail irgendwohin geht.</summary>
/// <remarks>
/// Geprüft wird die FORM. Ob die Adresse bekannt ist, beantwortet dieser
/// Endpunkt bewusst nicht — er antwortet für bekannte und unbekannte gleich,
/// sonst verriete er die Mitgliedschaft auf dieser Plattform, ohne den Ledger zu
/// fragen. Eine Formprüfung sieht nur die Zeichenkette und kann diese Zusage
/// deshalb gar nicht brechen.
/// <para>
/// Dieselbe Prüfung wie bei der Registrierung, und aus demselben Grund: dieser
/// Weg baut ebenfalls eine <c>MailMessage</c>. Ohne sie liefe „erneut senden"
/// an eine kaputte Adresse in denselben verschluckten Versandfehler — nur dass
/// die Antwort weiterhin 202 lautet.
/// </para>
/// </remarks>
public sealed class ErneutSendenPruefung : AbstractValidator<BestaetigungErneutSendenBefehl>
{
    /// <summary>Setzt die Regel.</summary>
    public ErneutSendenPruefung() =>
        RuleFor(befehl => befehl.Email)
            .NotEmpty().WithMessage("email is required")
            .Must(Emailadresse.IstZustellbar).WithMessage("email is not a deliverable address")
            .OverridePropertyName("email");
}
