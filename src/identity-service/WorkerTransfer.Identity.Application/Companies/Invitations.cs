using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Invite an address into a company.</summary>
/// <remarks>
/// An address, not an account. The invited person need not have one — and the
/// answer is the same either way, or the endpoint would be a way to ask about
/// platform membership without asking the consent ledger.
/// </remarks>
public sealed record InviteMemberCommand(
    SubjectId Subject, TenantId Company, string Email, MembershipRole Role) : ICommandMessage<Invitation>;

/// <inheritdoc cref="InviteMemberCommand" />
public sealed class InviteMemberHandler(
    CompanyAccess access,
    IInvitationRepository invitations,
    ICompanyRepository companies,
    IUserRepository users,
    IOneTimeToken oneTimeToken,
    IMailTray mailTray,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<InviteMemberCommand, Invitation>
{
    /// <inheritdoc />
    public async Task<Invitation> Handle(
        InviteMemberCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await access.AsAdminAsync(
            request.Subject, request.Company, AdministrativeAct.Invite, cancellationToken);

        var invitation = Invitation.Issue(
            request.Company, request.Email.Trim().ToLowerInvariant(), request.Role,
            role, request.Subject, clock.GetUtcNow());

        var (plainText, hash) = oneTimeToken.Create();
        await invitations.AddAsync(invitation, hash, cancellationToken);

        var company = await companies.FindByIdAsync(request.Company, cancellationToken);

        // The INVITER's account language, and that is a deliberate makeshift:
        // the invited address may well have no account here at all — that is
        // exactly what the invitation is for. So there is no row of theirs to
        // read. Whoever invites a colleague most likely writes to them in the
        // language they work in themselves.
        var inviterAccount = await users.FindByIdAsync(
            request.Subject, cancellationToken);

        mailTray.InvitationMail(
            invitation.Email,
            request.Subject,
            company?.Name ?? string.Empty,
            plainText,
            inviterAccount?.AccountLanguage ?? LanguageChoice.Default);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.MemberInvited,
                clock.GetUtcNow(),
                actor: request.Subject,
                tenant: request.Company,
                correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // The role is technical metadata about a permission, not a
                    // statement about the person — and the address is not on the
                    // allowlist for exactly that reason.
                    ["role"] = MembershipRoleNames.ToDatabase(request.Role)
                }),
            cancellationToken);

        return invitation;
    }
}

/// <summary>Take an open invitation back.</summary>
public sealed record RevokeInvitationCommand(SubjectId Subject, TenantId Company, Guid Invitation)
    : ICommandMessage<Unit>;

/// <inheritdoc cref="RevokeInvitationCommand" />
public sealed class RevokeInvitationHandler(
    CompanyAccess access,
    IInvitationRepository invitations,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<RevokeInvitationCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(
        RevokeInvitationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await access.AsAdminAsync(
            request.Subject, request.Company, AdministrativeAct.Invite, cancellationToken);

        // Looked up inside the company, never by id alone: otherwise an
        // administrator of one company could act on another company's
        // invitation.
        var invitation = await invitations.FindAsync(
            request.Company, request.Invitation, cancellationToken)
            ?? throw new InvitationInvalidException();

        invitation.Withdraw(role);
        await invitations.SaveAsync(invitation, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.InvitationWithdrawn,
                clock.GetUtcNow(),
                actor: request.Subject,
                tenant: request.Company,
                correlationId: correlation.Current),
            cancellationToken);

        return Unit.Value;
    }
}

/// <summary>Take an invitation up.</summary>
/// <remarks>
/// The token alone is not enough: tokens get forwarded, and whoever holds the
/// link is not necessarily who was invited. The address comes from the account,
/// not from the request.
/// </remarks>
public sealed record AcceptInvitationCommand(SubjectId Subject, string Token)
    : ICommandMessage<Membership>;

/// <inheritdoc cref="AcceptInvitationCommand" />
public sealed class AcceptInvitationHandler(
    IInvitationRepository invitations,
    IUserRepository users,
    IMembershipRepository memberships,
    ICompanyRepository companies,
    IOneTimeToken oneTimeToken,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<AcceptInvitationCommand, Membership>
{
    /// <inheritdoc />
    public async Task<Membership> Handle(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invitation = await invitations.FindByTokenHashAsync(
            oneTimeToken.HashOf(request.Token), cancellationToken)
            ?? throw new InvitationInvalidException();

        var account = await users.FindByIdAsync(request.Subject, cancellationToken)
            ?? throw new InvitationInvalidException();

        invitation.Accept(account.Email, clock.GetUtcNow());
        await invitations.SaveAsync(invitation, cancellationToken);

        await memberships.AddAsync(
            request.Subject, invitation.Tenant, invitation.Role, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.MemberJoined,
                clock.GetUtcNow(),
                actor: request.Subject,
                tenant: invitation.Tenant,
                correlationId: correlation.Current,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["role"] = MembershipRoleNames.ToDatabase(invitation.Role)
                }),
            cancellationToken);

        var company = await companies.FindByIdAsync(invitation.Tenant, cancellationToken);

        // Joining changes the memberships, not the running session: to act for
        // the new company one switches deliberately afterwards (ADR-0018). An
        // automatic switch would push somebody out of the company they are
        // working in, unasked.
        return new Membership(
            invitation.Tenant,
            company?.Name ?? string.Empty,
            company?.Domain.Value ?? string.Empty,
            invitation.Role);
    }
}
