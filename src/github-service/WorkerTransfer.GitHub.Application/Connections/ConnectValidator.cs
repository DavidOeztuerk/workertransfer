using FluentValidation;

namespace WorkerTransfer.GitHub.Application.Connections;

/// <summary>A GitHub login has to be there.</summary>
/// <remarks>
/// <para>What is checked is only THAT something stands there — not whether the
/// account exists. Only the challenge answers that, and through the person
/// themselves: this service holds exclusively a human being's own, proved
/// connection (ADR-0022).</para>
///
/// <para>No shape check against GitHub's naming rules. Whoever rebuilt them
/// here would have a second truth about which logins may exist — and it would
/// come apart the next time GitHub changes theirs.</para>
/// </remarks>
public sealed class ConnectValidator : AbstractValidator<ConnectCommand>
{
    /// <summary>Sets the one rule.</summary>
    public ConnectValidator() =>
        RuleFor(command => command.Login)
            .NotEmpty().WithMessage("login is required")
            .OverridePropertyName("login");
}
