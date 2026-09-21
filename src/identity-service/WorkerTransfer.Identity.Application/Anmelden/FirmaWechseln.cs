using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Sessions;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>Act for a company from now on.</summary>
/// <remarks>
/// The caller may name any company; membership decides. That is what keeps the
/// tenant out of client control even though the client names it — the tenant
/// that reaches a token was never taken from the request, it was derived from a
/// checked membership.
/// </remarks>
/// <param name="Subject">Who is asking — from the token, never from the body.</param>
/// <param name="Company">The company they name.</param>
public sealed record SwitchCompanyCommand(SubjectId Subject, TenantId Company)
    : ICommand<CompanySwitchResult>;

/// <summary>What the attempt to act for a company produced.</summary>
public abstract record CompanySwitchResult
{
    private CompanySwitchResult() { }

    /// <summary>A fresh pair, acting for the company.</summary>
    public sealed record Switched(
        string AccessToken,
        string RefreshToken,
        DateTimeOffset RefreshExpiresAt) : CompanySwitchResult;

    /// <summary>
    /// The caller is not a member of this company.
    /// </summary>
    /// <remarks>
    /// Answered the same whether or not the company exists. A distinct answer
    /// for "no such company" would let anyone probe which companies are here.
    /// </remarks>
    public sealed record NoMember : CompanySwitchResult;

    /// <summary>The account may not act at all.</summary>
    public sealed record Refused : CompanySwitchResult;
}

/// <summary>Mints a company-bound pair after verifying membership.</summary>
public sealed class SwitchCompanyHandler(
    IMembershipRepository memberships,
    IUserRepository users,
    ISessionService sessions,
    ISessionCapacity sessionCapacity,
    IAccessTokenIssuer token,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<SwitchCompanyCommand, CompanySwitchResult>
{
    /// <inheritdoc />
    public async Task<CompanySwitchResult> Handle(
        SwitchCompanyCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await memberships.IsMemberAsync(request.Subject, request.Company, cancellationToken))
        {
            await auditTrail.AppendAsync(
                new AuditEvent(
                    AuditAction.TenantSwitchDenied,
                    clock.GetUtcNow(),
                    actor: request.Subject,
                    // The company the caller asked for is the point of the
                    // record — especially because it was refused.
                    tenant: request.Company,
                    correlationId: correlation.Current),
                cancellationToken);

            return new CompanySwitchResult.NoMember();
        }

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);

        if (account is null)
        {
            return new CompanySwitchResult.Refused();
        }

        try
        {
            account.AssertCanSignIn();
        }
        catch (Exception error) when (error is EmailNotConfirmedException
                                              or AccountDisabledException)
        {
            return new CompanySwitchResult.Refused();
        }

        // A fresh sign-in rather than an edit of the old one: the personal
        // refresh token keeps working, and the company-bound one can be ended
        // on its own.
        var session = await sessions.StartAsync(account.Id, cancellationToken);
        var capacity = new Capacity.ForCompany(request.Company);

        await sessionCapacity.RememberAsync(session.Session, capacity, cancellationToken);

        var accessToken = await token.IssueAsync(
            account.Id, account.Email, capacity, session.Session, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.TenantSwitch,
                clock.GetUtcNow(),
                actor: account.Id,
                tenant: request.Company,
                correlationId: correlation.Current),
            cancellationToken);

        return new CompanySwitchResult.Switched(
            accessToken, session.RefreshToken, session.ExpiresAt);
    }
}
