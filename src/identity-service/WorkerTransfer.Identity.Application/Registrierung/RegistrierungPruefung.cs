using FluentValidation;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>What has to be there before an account can be created at all.</summary>
/// <remarks>
/// <para><strong>Found because it was missing.</strong> A body without
/// <c>display_name</c> ran all the way into the database and came back as a
/// <c>500</c> (<c>null value in column "display_name" violates not-null
/// constraint</c>). Exactly the class of fault the validation stage is meant to
/// close — registration was the only one of the four doors left without a
/// validator.</para>
///
/// <para><strong>It checks the form, never the existence.</strong> Whether the
/// address is known is something this endpoint deliberately does not answer: it
/// answers identically for known and unknown, or it would give away membership
/// of this platform without asking the ledger. A validator sees nothing but the
/// body and therefore cannot break that promise at all.</para>
///
/// <para><strong>Why the answer is 422 now instead of 400.</strong> Before, an
/// empty body fell into the domain layer (<c>Password rejected: must not be
/// empty</c>) and came back as 400 — as the only one of the four endpoints;
/// <c>/auth/login</c>, <c>/auth/verify-email</c> and
/// <c>/auth/resend-verification</c> have long answered 422. A malformed body is
/// the same thing everywhere and from now on is called the same everywhere.</para>
///
/// <para><strong>The address is checked for its form.</strong>
/// <c>NotEmpty()</c> alone let strings without an <c>@</c> through: the account
/// came into being, and only the sending failed — in a background run that
/// swallows the error. <see cref="EmailAddress.IsDeliverable"/> checks with the
/// same parser that sends later.</para>
///
/// <para><strong>No password measure here.</strong> How long and how composed a
/// password has to be is a domain rule and stands where it belongs. All that
/// stands here is that there is one — otherwise the rule would exist twice and
/// the two would diverge at the first change.</para>
///
/// <para><strong>The company name stays unchecked.</strong> It is optional, and
/// „not set" is the ordinary case: the usual human being on a transfer market
/// has no company.</para>
/// </remarks>
public sealed class RegistrationValidator : AbstractValidator<RegisterCommand>
{
    /// <summary>Sets the rules.</summary>
    public RegistrationValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("email is required")
            // The message names the rule, never the value: `ValidationBehavior`
            // logs it.
            .Must(EmailAddress.IsDeliverable).WithMessage("email is not a deliverable address")
            .OverridePropertyName("email");

        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("password is required")
            .OverridePropertyName("password");

        RuleFor(command => command.DisplayName)
            .NotEmpty().WithMessage("display_name is required")
            .OverridePropertyName("display_name");
    }
}
