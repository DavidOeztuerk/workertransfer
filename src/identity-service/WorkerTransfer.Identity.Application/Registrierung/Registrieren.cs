using Noelia.Abstractions.Security.Passwords;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Register an account.</summary>
/// <param name="OccupationalField">
/// Which working world the person stands in. <c>null</c> is the ordinary case.
/// </param>
/// <param name="CompanyName">
/// Set means "a company is registering here". It becomes a company only when
/// the address is confirmed — until then it is an intention (E2.6).
/// </param>
public sealed record RegisterCommand(
    string Email,
    string Password,
    string DisplayName,
    string? CompanyName = null,
    AccountLanguage AccountLanguage = LanguageChoice.Default,
    string? GivenName = null,
    string? FamilyName = null,
    OccupationalField? OccupationalField = null) : ICommand<RegistrationResult>;

/// <summary>What registering produced.</summary>
public abstract record RegistrationResult
{
    private RegistrationResult() { }

    /// <summary>
    /// Taken. Answered whether or not the address was already known.
    /// </summary>
    /// <remarks>
    /// One case for both, deliberately, and it is a single type so no endpoint
    /// can accidentally tell them apart. A 409 would answer "is this person
    /// here?" without asking the consent ledger — on a transfer market, the
    /// question that costs somebody their job. The real owner is told instead,
    /// by mail.
    /// </remarks>
    public sealed record Accepted : RegistrationResult;

    /// <summary>The password does not clear the floor.</summary>
    public sealed record WeakPassword(string Reason) : RegistrationResult;

    /// <summary>A mass provider cannot be claimed as a company.</summary>
    public sealed record PublicDomain(string Domain) : RegistrationResult;
}

/// <summary>Takes a registration.</summary>
public sealed class RegisterHandler(
    IUserRepository users,
    IPasswordHasher passwords,
    IVerificationTokenRepository tokens,
    IOneTimeToken oneTimeToken,
    IMailTray mailTray,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<RegisterCommand, RegistrationResult>
{
    /// <summary>How long a confirmation link works.</summary>
    private static readonly TimeSpan Validity = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<RegistrationResult> Handle(
        RegisterCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            PasswordPolicy.Validate(request.Password);
        }
        catch (WeakPasswordException weak)
        {
            return new RegistrationResult.WeakPassword(weak.Reason);
        }

        // Freemail BEFORE the existence check, and that is not a matter of
        // style: checked afterwards, the endpoint would answer differently for
        // a known address than for an unknown one — and the promise of "the
        // same answer" would be gone.
        //
        // Nothing is revealed by it: whether an address sits with a mass
        // provider is in the address the person just typed themselves.
        //
        // The domain ITSELF is not checked here. "firma.de is already claimed"
        // would be an enumeration channel over companies, answerable by anyone
        // who guesses a domain. That waits for the confirmation, when the
        // address is proven.
        if (request.CompanyName is not null)
        {
            var domain = EmailDomain.FromEmail(request.Email);

            if (domain.IsPublic)
            {
                return new RegistrationResult.PublicDomain(domain.Value);
            }
        }

        // Always hash, even when the address has long been taken. bcrypt with
        // twelve rounds costs a few hundred milliseconds; an early exit would
        // be back in ten and would give away over the clock what the identical
        // status code is hiding. Otherwise the enumeration channel just moves
        // into the wristwatch.
        var entry = passwords.Hash(request.Password);

        var existing = await users.FindByEmailAsync(request.Email, cancellationToken);

        if (existing is not null)
        {
            // The language of the EXISTING account, not that of the request:
            // this mail goes to the rightful owner, and they chose. The request
            // may well come from somebody else.
            mailTray.DuplicateRegistration(
                existing.Email, existing.Id, existing.AccountLanguage);
            return new RegistrationResult.Accepted();
        }

        var account = User.Register(
            request.Email, entry, request.DisplayName, request.CompanyName,
            request.AccountLanguage, request.GivenName, request.FamilyName,
            request.OccupationalField);

        await users.AddAsync(account, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.Register,
                clock.GetUtcNow(),
                // Registering is an act of a person, not of a company (ADR-0017).
                actor: account.Id,
                correlationId: correlation.Current),
            cancellationToken);

        var (plainText, hash) = oneTimeToken.Create();

        await tokens.AddAsync(
            new VerificationToken(
                Guid.CreateVersion7(),
                account.Id,
                hash,
                TokenPurpose.EmailVerify,
                clock.GetUtcNow() + Validity,
                ConsumedAt: null),
            cancellationToken);

        mailTray.ConfirmationLink(
            account.Email, account.Id, plainText, account.AccountLanguage);

        return new RegistrationResult.Accepted();
    }
}
