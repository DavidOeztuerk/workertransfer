using FluentValidation;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>An invitation token has to be there.</summary>
/// <remarks>
/// The order stays: authentication first, then the input. The endpoint refuses
/// a stranger before that — otherwise they would learn from the difference
/// 422/401 that this endpoint exists. The validator runs afterwards, in the
/// mediator.
/// </remarks>
public sealed class InvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    /// <summary>Sets the one rule.</summary>
    public InvitationValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage("token is required")
            .OverridePropertyName("token");
}

/// <summary>A company needs a name.</summary>
/// <remarks>
/// The domain is NOT checked here — it comes from the confirmed address and not
/// from the body, and whether it is already claimed is an answer (409) and not
/// a malformed request.
/// </remarks>
public sealed class CompanyFoundingValidator : AbstractValidator<FoundCompanyCommand>
{
    /// <summary>Sets the one rule.</summary>
    public CompanyFoundingValidator() =>
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("name is required")
            .OverridePropertyName("name");
}
