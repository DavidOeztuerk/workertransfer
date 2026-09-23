using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.SignIn;

/// <summary>Carry a sign-in forward with the refresh token presented.</summary>
public sealed record RefreshCommand(string RefreshToken) : ICommandMessage<RefreshResult>;

/// <summary>What a refresh produced.</summary>
public abstract record RefreshResult
{
    private RefreshResult() { }

    /// <summary>A fresh pair. The presented refresh token no longer counts.</summary>
    public sealed record Refreshed(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshExpiresAt) : RefreshResult;

    /// <summary>
    /// The presented token entitles the caller to nothing.
    /// </summary>
    /// <remarks>
    /// Expired, already used, never issued, or belonging to an account that may
    /// no longer sign in — one answer for all of them, and the caller is told to
    /// drop the cookie it presented.
    /// </remarks>
    public sealed record Rejected : RefreshResult;
}

/// <summary>Carries a sign-in forward.</summary>
/// <remarks>
/// Rotation is the point: every refresh retires the token it was given, so a
/// stolen one works once and the second use is visible.
/// </remarks>
public sealed class RefreshHandler(
    ISessionService sessions,
    IUserRepository users,
    IMembershipRepository memberships,
    ISessionCapacity sessionCapacity,
    IAccessTokenIssuer token,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<RefreshCommand, RefreshResult>
{
    /// <inheritdoc />
    public async Task<RefreshResult> Handle(
        RefreshCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await sessions.RenewAsync(request.RefreshToken, cancellationToken);

        if (session is null)
        {
            return new RefreshResult.Rejected();
        }

        var account = await users.FindByIdAsync(session.Subject, cancellationToken);

        if (account is null || !MayStillSignIn(account))
        {
            // A refresh mints a fresh access token, so it is an authorisation
            // decision and not a formality. Without this an account that was
            // disabled keeps working for as long as it keeps refreshing —
            // the Python service leaves that window open.
            return new RefreshResult.Rejected();
        }

        var capacity = await ReChecked(session, cancellationToken);

        var accessToken = await token.IssueAsync(
            account.Id, account.Email, capacity, session.Session, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.TokenRefresh,
                clock.GetUtcNow(),
                actor: account.Id,
                tenant: (capacity as Capacity.ForCompany)?.Tenant,
                correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "rotation"
                }),
            cancellationToken);

        return new RefreshResult.Refreshed(
            accessToken, session.RefreshToken, session.ExpiresAt);
    }

    /// <summary>
    /// The capacity this sign-in still holds — asked again rather than carried
    /// forward.
    /// </summary>
    /// <remarks>
    /// Checking the membership once, when the company token is first minted,
    /// makes the check good exactly once: whoever was let in stays in for as
    /// long as they keep refreshing, long after the company removed them.
    /// <para>
    /// Only the company falls away, never the sign-in. The person is still
    /// signed in and simply acts as themselves again, which is the default
    /// state (ADR-0017) — ending the session would sign someone out of their
    /// personal account because a company relationship ended.
    /// </para>
    /// </remarks>
    private async Task<Capacity> ReChecked(
        RenewedSession session,
        CancellationToken cancellationToken)
    {
        var capacity = await sessionCapacity.RecallAsync(session.Session, cancellationToken);

        if (capacity is not Capacity.ForCompany company)
        {
            return capacity;
        }

        if (await memberships.IsMemberAsync(session.Subject, company.Tenant, cancellationToken))
        {
            return capacity;
        }

        await sessionCapacity.RememberAsync(
            session.Session, Capacity.AsSelf.Instance, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.TenantSwitchDenied,
                clock.GetUtcNow(),
                actor: session.Subject,
                // The company that was dropped is the point of the record.
                tenant: company.Tenant,
                correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["reason"] = "membership_gone_on_refresh"
                }),
            cancellationToken);

        return Capacity.AsSelf.Instance;
    }

    private static bool MayStillSignIn(User account)
    {
        try
        {
            account.AssertCanSignIn();
            return true;
        }
        catch (EmailNotConfirmedException)
        {
            return false;
        }
        catch (AccountDisabledException)
        {
            return false;
        }
    }
}
