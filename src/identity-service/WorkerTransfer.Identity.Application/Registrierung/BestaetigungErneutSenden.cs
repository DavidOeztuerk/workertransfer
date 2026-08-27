using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Send the confirmation link again.</summary>
public sealed record BestaetigungErneutSendenBefehl(string Email) : IBefehl<Unit>;

/// <summary>Issues a fresh confirmation link, and voids the old ones.</summary>
/// <remarks>
/// Answers the same for an unknown address and for an account confirmed long
/// ago. A different answer here would be exactly the enumeration channel that
/// <c>register</c> is built to close.
/// </remarks>
public sealed class BestaetigungErneutSendenHandler(
    IUserRepository benutzer,
    IVerificationTokenRepository tokens,
    IEinmaltoken einmaltoken,
    IPostkorb postkorb,
    TimeProvider uhr)
    : IRequestHandler<BestaetigungErneutSendenBefehl, Unit>
{
    /// <summary>How long a confirmation link works.</summary>
    private static readonly TimeSpan Gueltigkeit = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public async Task<Unit> Handle(
        BestaetigungErneutSendenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var konto = await benutzer.FindByEmailAsync(request.Email, cancellationToken);

        if (konto is not { Status: AccountStatus.Pending })
        {
            // Nothing to do, and nothing to say. An account already confirmed
            // needs no new link.
            return Unit.Value;
        }

        var jetzt = uhr.GetUtcNow();

        // Otherwise any number of valid links stay in circulation at once, and
        // the oldest — possibly the one that went to the wrong mailbox — keeps
        // working.
        await tokens.ConsumeOpenAsync(
            konto.Id, TokenPurpose.EmailVerify, jetzt, cancellationToken);

        var (klartext, hash) = einmaltoken.Erzeuge();

        await tokens.AddAsync(
            new VerificationToken(
                Guid.CreateVersion7(),
                konto.Id,
                hash,
                TokenPurpose.EmailVerify,
                jetzt + Gueltigkeit,
                ConsumedAt: null),
            cancellationToken);

        postkorb.Bestaetigungslink(konto.Email, konto.Id, klartext);

        return Unit.Value;
    }
}
