using FluentValidation;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Ein Einladungstoken muss dasein.</summary>
/// <remarks>
/// Die Reihenfolge bleibt: erst Anmeldung, dann Eingabe. Der Endpunkt weist
/// einen Fremden schon vorher ab — sonst lernte er aus dem Unterschied 422/401,
/// dass es diesen Endpunkt gibt. Der Validator läuft danach, im Mediator.
/// </remarks>
public sealed class InvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    /// <summary>Setzt die eine Regel.</summary>
    public InvitationValidator() =>
        RuleFor(befehl => befehl.Token)
            .NotEmpty().WithMessage("token is required")
            .OverridePropertyName("token");
}

/// <summary>Eine Firma braucht einen Namen.</summary>
/// <remarks>
/// Die Domäne wird NICHT hier geprüft — sie stammt aus der bestätigten Adresse
/// und nicht aus dem Rumpf, und ob sie schon beansprucht ist, ist eine Antwort
/// (409) und keine kaputte ResumeRequest.
/// </remarks>
public sealed class CompanyFoundingValidator : AbstractValidator<UnternehmenGruendenBefehl>
{
    /// <summary>Setzt die eine Regel.</summary>
    public CompanyFoundingValidator() =>
        RuleFor(befehl => befehl.Name)
            .NotEmpty().WithMessage("name is required")
            .OverridePropertyName("name");
}
