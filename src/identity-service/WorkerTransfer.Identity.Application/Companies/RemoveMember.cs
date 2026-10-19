using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Remove a member — or oneself.</summary>
public sealed record RemoveMemberCommand(SubjectId Subject, TenantId Company, SubjectId Member)
    : ICommandMessage<Unit>;

/// <inheritdoc cref="RemoveMemberCommand" />
/// <remarks>
/// Takes effect on the next refresh, which checks the membership again. An
/// access token already issued stays valid until it expires — the same known
/// remainder as signing out.
/// </remarks>
public sealed class RemoveMemberHandler(
    CompanyAccess access,
    IMembershipRepository memberships,
    IAuditTrail auditTrail,
    ICorrelation correlation,
    TimeProvider clock)
    : IRequestHandler<RemoveMemberCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(
        RemoveMemberCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var self = request.Subject == request.Member;

        // Leaving on your own needs no administrator. Requiring one would mean
        // somebody could be held in a company by the company.
        if (!self)
        {
            await access.AsAdminAsync(
                request.Subject, request.Company, AdministrativeAct.Remove, cancellationToken);
        }

        var role = await memberships.RoleOfAsync(
            request.Member, request.Company, cancellationToken)
            ?? throw new NotAMemberException();

        if (role == MembershipRole.Admin
            && await memberships.CountAdminsAsync(request.Company, cancellationToken) <= 1)
        {
            throw new LastAdminMayNotLeaveException();
        }

        await memberships.RemoveAsync(request.Member, request.Company, cancellationToken);

        await auditTrail.AppendAsync(
            new AuditEvent(
                AuditAction.MemberRemoved,
                clock.GetUtcNow(),
                actor: request.Subject,
                tenant: request.Company,
                target: request.Member,
                correlationId: correlation.Current),
            cancellationToken);

        return Unit.Value;
    }
}
