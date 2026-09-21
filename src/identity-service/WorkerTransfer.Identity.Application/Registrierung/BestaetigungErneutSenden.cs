using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Send the confirmation link again.</summary>
public sealed record ResendConfirmationCommand(string Email) : ICommand<Unit>;

/// <summary>Issues a fresh confirmation link, and voids the old ones.</summary>
/// <remarks>
/// Answers the same for an unknown address and for an account confirmed long
/// ago. A different answer here would be exactly the enumeration channel that
/// <c>register</c> is built to close.
/// </remarks>
public sealed class ResendConfirmationHandler(
    IUserRepository users,
    IVerificationTokenRepository tokens,
    IOneTimeToken oneTimeToken,
    IMailTray mailTray,
    TimeProvider clock)
    : IRequestHandler<ResendConfirmationCommand, Unit>
{
    /// <summary>How long a confirmation link works.</summary>
    private static readonly TimeSpan Validity = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<Unit> Handle(
        ResendConfirmationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByEmailAsync(request.Email, cancellationToken);

        if (account is not { Status: AccountStatus.Pending })
        {
            // Nothing to do, and nothing to say. An account already confirmed
            // needs no new link.
            return Unit.Value;
        }

        var now = clock.GetUtcNow();

        // Otherwise any number of valid links stay in circulation at once, and
        // the oldest — possibly the one that went to the wrong mailbox — keeps
        // working.
        await tokens.ConsumeOpenAsync(
            account.Id, TokenPurpose.EmailVerify, now, cancellationToken);

        var (plainText, hash) = oneTimeToken.Create();

        await tokens.AddAsync(
            new VerificationToken(
                Guid.CreateVersion7(),
                account.Id,
                hash,
                TokenPurpose.EmailVerify,
                now + Validity,
                ConsumedAt: null),
            cancellationToken);

        mailTray.ConfirmationLink(
            account.Email, account.Id, plainText, account.AccountLanguage);

        return Unit.Value;
    }
}
