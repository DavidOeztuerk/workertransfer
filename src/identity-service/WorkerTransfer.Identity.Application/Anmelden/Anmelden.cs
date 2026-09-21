using Noelia.Abstractions.Security.Passwords;
using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Sign in with an address and a password.</summary>
public sealed record SignInCommand(string Email, string Password) : ICommand<SignInResult>;

/// <summary>What a sign-in attempt produced.</summary>
public abstract record SignInResult
{
    private SignInResult() { }

    /// <summary>Signed in. Both tokens are here and nowhere else.</summary>
    public sealed record SignedIn(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshExpiresAt) : SignInResult;

    /// <summary>
    /// Wrong address, wrong password, or an account that may not sign in.
    /// </summary>
    /// <remarks>
    /// One answer for all of them. A separate one per case answers "is this
    /// person here?" to whoever asks, and on a transfer market that is the
    /// question that costs someone their job. Which case it was is in the audit
    /// trail, where only an operator sees it.
    /// </remarks>
    public sealed record Refused : SignInResult;

    /// <summary>The password was right and the address is not confirmed yet.</summary>
    /// <remarks>
    /// Its own answer, so the interface can offer to send the mail again.
    /// Reached only after the password verified, so it states nothing the
    /// password did not already prove.
    /// </remarks>
    public sealed record NotConfirmed : SignInResult;
}

/// <summary>Signs a person in.</summary>
public sealed class SignInHandler(
    IUserRepository users,
    IPasswordHasher passwords,
    ISessionService sessions,
    IAccessTokenIssuer token,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<SignInCommand, SignInResult>
{
    /// <inheritdoc />
    public async Task<SignInResult> Handle(
        SignInCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByEmailAsync(request.Email, cancellationToken);

        // Null is a supported input: the implementation spends the same work
        // before answering Failed, so how long an answer takes says nothing
        // about whether the address is known. Reordering these two operands
        // brings the timing channel back.
        if (passwords.Verify(request.Password, account?.PasswordHash) == PasswordVerification.Failed
            || account is null)
        {
            await RecordRefusal(
                account is null ? "unknown_user" : "bad_password", account?.Id, cancellationToken);
            return new SignInResult.Refused();
        }

        try
        {
            account.AssertCanSignIn();
        }
        catch (EmailNotConfirmedException)
        {
            await RecordRefusal("email_not_confirmed", account.Id, cancellationToken);
            return new SignInResult.NotConfirmed();
        }
        catch (AccountDisabledException)
        {
            await RecordRefusal("disabled", account.Id, cancellationToken);
            return new SignInResult.Refused();
        }

        var session = await sessions.StartAsync(account.Id, cancellationToken);

        // Signing in makes you yourself, never a company. Acting for one is a
        // second step that verifies membership before a tenant reaches a token.
        var accessToken = await token.IssueAsync(
            account.Id,
            account.Email,
            Capacity.AsSelf.Instance,
            session.Session,
            cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.LoginSuccess,
                clock.GetUtcNow(),
                actor: account.Id,
                correlationId: correlation.Current),
            cancellationToken);

        return new SignInResult.SignedIn(
            accessToken, session.RefreshToken, session.ExpiresAt);
    }

    /// <summary>
    /// Records why a sign-in was refused — the one place the four cases are
    /// told apart, and it is not the answer.
    /// </summary>
    private Task RecordRefusal(
        string reason,
        SubjectId? subject,
        CancellationToken cancellationToken) =>
        auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.LoginFailure,
                clock.GetUtcNow(),
                actor: subject,
                correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = reason
                }),
            cancellationToken);
}
