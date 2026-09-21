using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.Registrierung;

/// <summary>Confirm an address with the token from the mail.</summary>
public sealed record ConfirmAddressCommand(string Token) : ICommand<ConfirmationResult>;

/// <summary>What confirming produced.</summary>
public abstract record ConfirmationResult
{
    private ConfirmationResult() { }

    /// <summary>
    /// The account is active.
    /// </summary>
    /// <remarks>
    /// Three exits, not two: confirmed · confirmed <em>with</em> a company ·
    /// confirmed <em>without</em> one and why. The third is the one an
    /// interface otherwise shows as "all good" while half the intention has
    /// evaporated.
    /// </remarks>
    /// <param name="CompanyName">The company that was created, if one was.</param>
    /// <param name="CompanyError">Why none was, if the intention was refused.</param>
    public sealed record Confirmed(string? CompanyName = null, string? CompanyError = null)
        : ConfirmationResult;

    /// <summary>The link means nothing.</summary>
    public sealed record Invalid : ConfirmationResult;

    /// <summary>The link is past its time.</summary>
    public sealed record Expired : ConfirmationResult;
}

/// <summary>Confirms an address, and redeems the company intention with it.</summary>
public sealed class ConfirmAddressHandler(
    IUserRepository users,
    IVerificationTokenRepository tokens,
    IOneTimeToken oneTimeToken,
    CreateCompany createCompany,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<ConfirmAddressCommand, ConfirmationResult>
{
    /// <inheritdoc />
    public async Task<ConfirmationResult> Handle(
        ConfirmAddressCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();
        var row = await tokens.FindByHashAsync(
            oneTimeToken.HashOf(request.Token), cancellationToken);

        if (row is not { Purpose: TokenPurpose.EmailVerify })
        {
            // An unknown token says nothing about why it is unknown, or the
            // endpoint becomes an oracle.
            return new ConfirmationResult.Invalid();
        }

        var account = await users.FindByIdAsync(row.Subject, cancellationToken);

        if (account is null)
        {
            return new ConfirmationResult.Invalid();
        }

        if (row.IsConsumed)
        {
            // Clicking the same link twice is not an error: the account is
            // exactly as unlocked as the click wanted it. Whoever holds the
            // token had the mail, so nothing is revealed here.
            //
            // Still pending means the token was voided by a resend, and the old
            // link must not unlock anything any more.
            return account.Status == AccountStatus.Active
                ? new ConfirmationResult.Confirmed()
                : new ConfirmationResult.Invalid();
        }

        if (row.IsExpired(now))
        {
            return new ConfirmationResult.Expired();
        }

        account.Activate();

        // Without this the unlock stays in memory: the aggregate comes back
        // detached from the repository.
        await users.SaveAsync(account, cancellationToken);
        await tokens.ConsumeAsync(row.Id, now, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.EmailVerified,
                now,
                actor: account.Id,
                correlationId: correlation.Current),
            cancellationToken);

        return account.PendingCompanyName is { } intended
            ? await RedeemIntent(account, intended, cancellationToken)
            : new ConfirmationResult.Confirmed();
    }

    /// <summary>
    /// Now — and only now — the intention from the registration can be
    /// redeemed.
    /// </summary>
    /// <remarks>
    /// The account is active, so the company creation accepts it, and the
    /// domain comes from a <em>proven</em> address (ADR-0019). Same
    /// transaction, same repositories, no service-to-service call: companies
    /// live in this service.
    /// </remarks>
    private async Task<ConfirmationResult> RedeemIntent(
        User account,
        string intended,
        CancellationToken cancellationToken)
    {
        var result = await createCompany.ExecuteOneAsync(account, intended, cancellationToken);

        // Spent in EVERY case, including a refusal — see User.CompanyIntentSpent.
        account.CompanyIntentSpent();
        await users.SaveAsync(account, cancellationToken);

        return result switch
        {
            CompanyResult.Created created =>
                new ConfirmationResult.Confirmed(CompanyName: created.Company.Name),

            // The CONFIRMATION does not fail over this. The account is
            // unlocked, and that is right and irreversible: locking somebody
            // out because a company name was taken would be the wrong answer to
            // the wrong question.
            CompanyResult.Refused refused =>
                new ConfirmationResult.Confirmed(CompanyError: refused.Code),

            _ => new ConfirmationResult.Confirmed()
        };
    }
}
