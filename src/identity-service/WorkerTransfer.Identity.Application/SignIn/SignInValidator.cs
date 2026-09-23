using FluentValidation;

namespace WorkerTransfer.Identity.Application.SignIn;

/// <summary>
/// The first real validator of this system.
/// </summary>
/// <remarks>
/// <para><strong>The stage was always there, only empty.</strong> Noelia's
/// <c>AddCQRS</c> hangs <c>ValidationBehavior</c> in every pipeline and calls
/// <c>AddValidatorsFromAssemblies</c> over exactly this assembly. The behaviour
/// drops straight out again for as long as it finds no validator — and it found
/// none. So it was never a wiring that was missing, but the content.</para>
///
/// <para><strong>Why here and not at the endpoint.</strong> Until D2 this check
/// stood in the endpoint, because the pipeline stage was held to be dead. It is
/// not. Here it stands more correctly: at the command, where it also holds for
/// a second caller and not only for the one endpoint.</para>
///
/// <para><strong>The messages name the rule, never the value.</strong> That is
/// no matter of style here: <c>ValidationBehavior</c> writes
/// <c>f.ErrorMessage</c> into the log, and FluentValidation's default messages
/// insert placeholders such as <c>{PropertyValue}</c>. A default message would
/// therefore be a value in the log. So every rule here carries one of its own,
/// and the answer outwards names nothing but field names
/// (<c>ProblemDetailsMiddleware</c>).</para>
///
/// <para><strong>The field names are the caller's.</strong>
/// <c>ProblemDetailsMiddleware</c> answers with <c>invalid: &lt;fields&gt;</c>,
/// and those names come out of the validator. Where our command is called
/// something different from what stood on the wire, it is renamed — otherwise
/// the answer names a field the caller never sent.</para>
///
/// <para>No check on the e-mail <em>form</em>: whether the address exists is
/// decided by the sending, and a form check answering 422 instead of 401 would
/// give away that the sign-in got that far at all.</para>
/// </remarks>
public sealed class SignInValidator : AbstractValidator<SignInCommand>
{
    /// <summary>Sets the two rules.</summary>
    public SignInValidator()
    {
        RuleFor(command => command.Email)
            .NotEmpty().WithMessage("email is required");

        // The name the CALLER sent, not ours. The answer says
        // `invalid: password`, and an `invalid: Passwort` would be, for
        // somebody who sent `password`, a field that does not exist.
        RuleFor(command => command.Password)
            .NotEmpty().WithMessage("password is required")
            .OverridePropertyName("password");
    }
}
