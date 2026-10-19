using FluentValidation;

namespace WorkerTransfer.Applications.Application.Applications;

/// <summary>An application state must be present.</summary>
/// <remarks>
/// Only THAT, not WHICH. Whether the state is one that exists, and whether the
/// transition there is allowed, is decided by the aggregate — the order of the
/// states stands there, and a second list here would diverge at the first new
/// state.
/// </remarks>
public sealed class StateValidator : AbstractValidator<MoveApplicationCommand>
{
    /// <summary>Sets the one rule.</summary>
    public StateValidator() =>
        RuleFor(command => command.State)
            .NotEmpty().WithMessage("status is required")
            .OverridePropertyName("status");
}
