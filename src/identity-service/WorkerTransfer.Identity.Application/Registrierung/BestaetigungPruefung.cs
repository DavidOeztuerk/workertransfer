using FluentValidation;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>A confirmation token has to be there before anybody looks it up.</summary>
/// <remarks>
/// <para>Before, the check stood in the endpoint, because the pipeline stage
/// was held to be dead. It is not — it was empty. At the command it also holds
/// for a second caller.</para>
///
/// <para><strong>Empty is not invalid.</strong> A missing token is a malformed
/// request (422); a token that does not exist is an answer and belongs in the
/// handler. Mixing the two would make „you sent nothing" and „this link is
/// spent" the same piece of information.</para>
/// </remarks>
public sealed class ConfirmationValidator : AbstractValidator<ConfirmAddressCommand>
{
    /// <summary>Sets the one rule.</summary>
    public ConfirmationValidator() =>
        RuleFor(command => command.Token)
            .NotEmpty().WithMessage("token is required")
            .OverridePropertyName("token");
}

/// <summary>An address has to be deliverable for the mail to go anywhere.</summary>
/// <remarks>
/// What is checked is the FORM. Whether the address is known is something this
/// endpoint deliberately does not answer — it answers identically for known and
/// unknown, or it would give away membership of this platform without asking
/// the ledger. A form check sees nothing but the string and therefore cannot
/// break that promise at all.
/// <para>
/// The same form check as at registration: this path builds a
/// <c>MailMessage</c> too.
/// </para>
/// </remarks>
public sealed class ResendValidator : AbstractValidator<ResendConfirmationCommand>
{
    /// <summary>Sets the rule.</summary>
    public ResendValidator() =>
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("email is required")
            .Must(EmailAddress.IsDeliverable).WithMessage("email is not a deliverable address")
            .OverridePropertyName("email");
}
