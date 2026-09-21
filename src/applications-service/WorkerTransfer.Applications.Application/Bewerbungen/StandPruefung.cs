using FluentValidation;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>Ein ApplicationState muss dasein.</summary>
/// <remarks>
/// Nur DASS, nicht WELCHER. Ob der State einer ist, den es gibt, und ob der
/// Übergang dorthin erlaubt ist, entscheidet das Aggregat — dort steht die
/// Reihenfolge der Stände, und eine zweite Liste hier ginge beim ersten neuen
/// State auseinander.
/// </remarks>
public sealed class StateValidator : AbstractValidator<MoveApplicationCommand>
{
    /// <summary>Setzt die eine Regel.</summary>
    public StateValidator() =>
        RuleFor(befehl => befehl.State)
            .NotEmpty().WithMessage("status is required")
            .OverridePropertyName("status");
}
