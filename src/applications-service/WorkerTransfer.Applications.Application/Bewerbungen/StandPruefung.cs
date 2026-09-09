using FluentValidation;

namespace WorkerTransfer.Applications.Application.Bewerbungen;

/// <summary>Ein Bewerbungsstand muss dasein.</summary>
/// <remarks>
/// Nur DASS, nicht WELCHER. Ob der Stand einer ist, den es gibt, und ob der
/// Übergang dorthin erlaubt ist, entscheidet das Aggregat — dort steht die
/// Reihenfolge der Stände, und eine zweite Liste hier ginge beim ersten neuen
/// Stand auseinander.
/// </remarks>
public sealed class StandPruefung : AbstractValidator<BewerbungBewegenBefehl>
{
    /// <summary>Setzt die eine Regel.</summary>
    public StandPruefung() =>
        RuleFor(befehl => befehl.Stand)
            .NotEmpty().WithMessage("status is required")
            .OverridePropertyName("status");
}
