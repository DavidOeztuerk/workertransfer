using FluentValidation;

namespace WorkerTransfer.GitHub.Application.Connections;

/// <summary>Ein GitHub-Login muss dasein.</summary>
/// <remarks>
/// <para>Geprüft wird nur, DASS etwas dasteht — nicht, ob es das Konto gibt.
/// Das beantwortet erst die Challenge, und zwar durch die Person selbst: dieser
/// Dienst hält ausschließlich die eigene, bewiesene Connection eines Menschen
/// (ADR-0022).</para>
///
/// <para>None Formprüfung auf GitHubs Namensregeln. Wer sie hier nachbaute,
/// hätte eine zweite Wahrheit darüber, welche Logins es geben darf — und die
/// ginge beim nächsten Mal auseinander, wenn GitHub seine ändert.</para>
/// </remarks>
public sealed class ConnectValidator : AbstractValidator<ConnectCommand>
{
    /// <summary>Setzt die eine Regel.</summary>
    public ConnectValidator() =>
        RuleFor(befehl => befehl.Login)
            .NotEmpty().WithMessage("login is required")
            .OverridePropertyName("login");
}
