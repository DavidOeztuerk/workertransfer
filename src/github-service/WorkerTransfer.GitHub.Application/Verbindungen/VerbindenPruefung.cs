using FluentValidation;

namespace WorkerTransfer.GitHub.Application.Verbindungen;

/// <summary>Ein GitHub-Login muss dasein.</summary>
/// <remarks>
/// <para>Geprüft wird nur, DASS etwas dasteht — nicht, ob es das Konto gibt.
/// Das beantwortet erst die Challenge, und zwar durch die Person selbst: dieser
/// Dienst hält ausschließlich die eigene, bewiesene Verbindung eines Menschen
/// (ADR-0022).</para>
///
/// <para>Keine Formprüfung auf GitHubs Namensregeln. Wer sie hier nachbaute,
/// hätte eine zweite Wahrheit darüber, welche Logins es geben darf — und die
/// ginge beim nächsten Mal auseinander, wenn GitHub seine ändert.</para>
/// </remarks>
public sealed class VerbindenPruefung : AbstractValidator<VerbindenBefehl>
{
    /// <summary>Setzt die eine Regel.</summary>
    public VerbindenPruefung() =>
        RuleFor(befehl => befehl.Login)
            .NotEmpty().WithMessage("login is required")
            .OverridePropertyName("login");
}
