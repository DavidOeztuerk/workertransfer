using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Application.SignIn;

/// <summary>The providers' side of a sign-in (ADR-0053).</summary>
public interface IExternalProviders
{
    /// <summary>The providers this instance has credentials for — the page shows a button for each.</summary>
    IReadOnlyList<ExternalProvider> Configured { get; }

    /// <summary>Where the browser is sent to sign in.</summary>
    Uri AuthorizationUrl(ExternalProvider provider, string state, string codeChallenge, string nonce);

    /// <summary>Exchanges the code and checks the token — the person, or <c>null</c> when the answer does not hold.</summary>
    /// <exception cref="ExternalProviderSilent">The provider did not answer.</exception>
    Task<ExternalIdentity?> ExchangeAsync(
        ExternalProvider provider, string code, string codeVerifier, string nonce, CancellationToken cancellationToken = default);
}

/// <summary>A provider did not answer — a system state, never a refusal of the person.</summary>
public sealed class ExternalProviderSilent(string reason, Exception? cause = null) : Exception(reason, cause);

/// <summary>Why a sign-in flow was started.</summary>
public enum ExternalIntent
{
    /// <summary>To sign in, or to create a person's account.</summary>
    SignIn,

    /// <summary>To bind the provider to the account already signed in.</summary>
    Link
}

/// <summary>
/// Exchanges a provider's code — a query, because it talks to the provider for
/// seconds and no transaction should wait on it.
/// </summary>
public sealed record ExchangeExternalCodeQuery(
    ExternalProvider Provider, string Code, string CodeVerifier, string Nonce) : IQueryMessage<ExternalIdentity?>;

/// <summary>Decides what a checked identity does: sign in, create, link — or nothing.</summary>
public sealed record ExternalSignInCommand(
    ExternalIdentity Identity,
    ExternalIntent Intent,
    SubjectId? SignedIn,
    AccountLanguage Language) : ICommandMessage<ExternalSignInResult>;

/// <summary>How a provider's sign-in ended.</summary>
public abstract record ExternalSignInResult
{
    private ExternalSignInResult()
    {
    }

    /// <summary>Signed in — both tokens are here and nowhere else.</summary>
    public sealed record SignedIn(string AccessToken, string RefreshToken, DateTimeOffset RefreshExpiresAt) : ExternalSignInResult;

    /// <summary>The provider now signs this account in too.</summary>
    public sealed record Linked : ExternalSignInResult;

    /// <summary>A mail is on its way — the same answer whether or not the address had an account.</summary>
    public sealed record CheckMail : ExternalSignInResult;

    /// <summary>The proven address already has an account; it is not linked by the address.</summary>
    public sealed record AddressTaken : ExternalSignInResult;

    /// <summary>This provider id belongs to another account, or this account already has this provider.</summary>
    public sealed record LinkTaken : ExternalSignInResult;

    /// <summary>Nothing happens — a disabled account, or no address to go on.</summary>
    public sealed record Refused : ExternalSignInResult;
}

/// <summary>Takes a provider off the signed-in account.</summary>
public sealed record UnlinkExternalCommand(SubjectId User, ExternalProvider Provider) : ICommandMessage<UnlinkResult>;

/// <summary>How taking a provider off ended.</summary>
public enum UnlinkResult
{
    /// <summary>Taken off.</summary>
    Unlinked,

    /// <summary>It was not linked.</summary>
    NotLinked,

    /// <summary>It is the account's only way in — no password, no other provider.</summary>
    LastWayIn
}

/// <summary>The account's ways in, for the settings.</summary>
public sealed record ExternalLinksQuery(SubjectId User) : IQueryMessage<ExternalLinks>;

/// <summary>What the settings show: linked providers, and whether a password exists.</summary>
public sealed record ExternalLinks(IReadOnlyList<ExternalLogin> Linked, bool HasPassword, IReadOnlyList<ExternalProvider> Available);

/// <summary>The provider sign-in (ADR-0053).</summary>
/// <remarks>
/// <para><strong>Linked by the provider's id, never by the address.</strong>
/// An unknown id whose proven address already has an account is not linked:
/// otherwise whoever can enter a foreign address at a provider takes over the
/// account behind it. Binding a provider to an existing account happens only
/// while signed in.</para>
///
/// <para><strong>Microsoft's address is not proven</strong> — a new account
/// from there gets our own confirmation mail, and a known address the
/// registration's "somebody tried" mail; the answer is the same either way, so
/// it reveals nothing about who has an account.</para>
/// </remarks>
public sealed class ExternalSignInHandlers(
    IExternalProviders providers,
    IExternalLoginStore logins,
    IUserRepository users,
    ISessionService sessions,
    IAccessTokenIssuer token,
    IVerificationTokenRepository verificationTokens,
    IOneTimeToken oneTimeToken,
    IMailTray mailTray,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock) :
    IRequestHandler<ExchangeExternalCodeQuery, ExternalIdentity?>,
    IRequestHandler<ExternalSignInCommand, ExternalSignInResult>,
    IRequestHandler<UnlinkExternalCommand, UnlinkResult>,
    IRequestHandler<ExternalLinksQuery, ExternalLinks>
{
    private static readonly TimeSpan ConfirmationValidity = TimeSpan.FromHours(24);

    /// <inheritdoc />
    public Task<ExternalIdentity?> Handle(ExchangeExternalCodeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return providers.ExchangeAsync(request.Provider, request.Code, request.CodeVerifier, request.Nonce, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ExternalSignInResult> Handle(ExternalSignInCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identity = request.Identity;
        var link = await logins.FindAsync(identity.Provider, identity.Subject, cancellationToken);

        if (request.Intent == ExternalIntent.Link)
        {
            return await LinkAsync(identity, link, request.SignedIn, cancellationToken);
        }

        if (link is not null)
        {
            if (await users.FindByIdAsync(link.User, cancellationToken) is not { } linkedAccount)
            {
                await Refusal("unknown_user", link.User, cancellationToken);
                return new ExternalSignInResult.Refused();
            }

            return await SignInAsync(linkedAccount, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(identity.Email) || !EmailAddress.IsDeliverable(identity.Email))
        {
            await Refusal("no_address", null, cancellationToken);
            return new ExternalSignInResult.Refused();
        }

        var email = identity.Email.Trim();
        var existing = await users.FindByEmailAsync(email, cancellationToken);

        if (existing is not null)
        {
            if (identity.EmailProven)
            {
                // The caller proved the address at the provider: telling them
                // it has an account says nothing they do not own.
                await Refusal("address_taken", existing.Id, cancellationToken);
                return new ExternalSignInResult.AddressTaken();
            }

            mailTray.DuplicateRegistration(existing.Email, existing.Id, existing.AccountLanguage);
            return new ExternalSignInResult.CheckMail();
        }

        // No password: this account signs in through the provider until its
        // owner sets one. `SignInHandler` treats the empty entry as none.
        var account = User.Register(
            email, string.Empty, identity.DisplayName, pendingCompanyName: null, request.Language,
            identity.GivenName, identity.FamilyName);

        if (identity.EmailProven)
        {
            account.Activate();
        }

        await users.AddAsync(account, cancellationToken);
        await logins.AddAsync(
            new ExternalLogin(identity.Provider, identity.Subject, account.Id, clock.GetUtcNow()), cancellationToken);
        await Audit(AuditAction.Register, account.Id, cancellationToken);

        if (identity.EmailProven)
        {
            // The account itself, not a read of it: inside this transaction
            // the row is not written yet, and a read would find nobody.
            return await SignInAsync(account, cancellationToken);
        }

        var (plainText, hash) = oneTimeToken.Create();
        await verificationTokens.AddAsync(
            new VerificationToken(
                Guid.CreateVersion7(), account.Id, hash, TokenPurpose.EmailVerify,
                clock.GetUtcNow() + ConfirmationValidity, ConsumedAt: null),
            cancellationToken);
        mailTray.ConfirmationLink(account.Email, account.Id, plainText, account.AccountLanguage);

        return new ExternalSignInResult.CheckMail();
    }

    /// <inheritdoc />
    public async Task<UnlinkResult> Handle(UnlinkExternalCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var linked = await logins.ForUserAsync(request.User, cancellationToken);

        if (linked.All(login => login.Provider != request.Provider))
        {
            return UnlinkResult.NotLinked;
        }

        var account = await users.FindByIdAsync(request.User, cancellationToken);

        // Never the last way in: without a password and without another
        // provider the account could not be entered again.
        if (string.IsNullOrEmpty(account?.PasswordHash) && linked.Count == 1)
        {
            return UnlinkResult.LastWayIn;
        }

        await logins.DeleteAsync(request.User, request.Provider, cancellationToken);
        await Audit(AuditAction.ExternalUnlinked, request.User, cancellationToken);
        return UnlinkResult.Unlinked;
    }

    /// <inheritdoc />
    public async Task<ExternalLinks> Handle(ExternalLinksQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.User, cancellationToken);
        var linked = await logins.ForUserAsync(request.User, cancellationToken);

        return new ExternalLinks(linked, !string.IsNullOrEmpty(account?.PasswordHash), providers.Configured);
    }

    private async Task<ExternalSignInResult> LinkAsync(
        ExternalIdentity identity, ExternalLogin? link, SubjectId? signedIn, CancellationToken cancellationToken)
    {
        if (signedIn is not { } user)
        {
            return new ExternalSignInResult.Refused();
        }

        if (link is not null)
        {
            return link.User == user ? new ExternalSignInResult.Linked() : new ExternalSignInResult.LinkTaken();
        }

        if ((await logins.ForUserAsync(user, cancellationToken)).Any(login => login.Provider == identity.Provider))
        {
            return new ExternalSignInResult.LinkTaken();
        }

        await logins.AddAsync(new ExternalLogin(identity.Provider, identity.Subject, user, clock.GetUtcNow()), cancellationToken);
        await Audit(AuditAction.ExternalLinked, user, cancellationToken);
        return new ExternalSignInResult.Linked();
    }

    private async Task<ExternalSignInResult> SignInAsync(User account, CancellationToken cancellationToken)
    {
        try
        {
            account.AssertCanSignIn();
        }
        catch (EmailNotConfirmedException)
        {
            // A Microsoft account waiting for its confirmation mail.
            await Refusal("email_not_confirmed", account.Id, cancellationToken);
            return new ExternalSignInResult.CheckMail();
        }
        catch (AccountDisabledException)
        {
            await Refusal("disabled", account.Id, cancellationToken);
            return new ExternalSignInResult.Refused();
        }

        var session = await sessions.StartAsync(account.Id, cancellationToken);

        // Signing in makes you yourself, never a company (ADR-0017).
        var accessToken = await token.IssueAsync(
            account.Id, account.Email, Capacity.AsSelf.Instance, session.Session, cancellationToken);

        await Audit(AuditAction.LoginSuccess, account.Id, cancellationToken);
        return new ExternalSignInResult.SignedIn(accessToken, session.RefreshToken, session.ExpiresAt);
    }

    // Which provider it was is not recorded: the audit metadata names
    // decisions, not where somebody holds accounts (`AuditEvent.AllowedMetadata`).
    private Task Audit(AuditAction action, SubjectId actor, CancellationToken cancellationToken) =>
        auditTrail.AppendAsync(
            new AuditEvent(action, clock.GetUtcNow(), actor: actor, correlationId: correlation.Current),
            cancellationToken);

    private Task Refusal(string reason, SubjectId? actor, CancellationToken cancellationToken) =>
        auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.LoginFailure, clock.GetUtcNow(), actor: actor, correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason }),
            cancellationToken);
}
