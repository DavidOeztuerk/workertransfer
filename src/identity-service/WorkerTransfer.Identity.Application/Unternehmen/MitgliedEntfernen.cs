using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Remove a member — or oneself.</summary>
public sealed record RemoveMemberCommand(SubjectId Wer, TenantId Firma, SubjectId Mitglied)
    : IBefehl<Unit>;

/// <inheritdoc cref="MitgliedEntfernenBefehl" />
/// <remarks>
/// Takes effect on the next refresh, which checks the membership again. An
/// access token already issued stays valid until it expires — the same known
/// remainder as signing out.
/// </remarks>
public sealed class RemoveMemberHandler(
    CompanyAccess zugriff,
    IMembershipRepository mitgliedschaften,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<RemoveMemberCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(
        RemoveMemberCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selbst = request.Wer == request.Mitglied;

        // Leaving on your own needs no administrator. Requiring one would mean
        // somebody could be held in a company by the company.
        if (!selbst)
        {
            await zugriff.AsAdminAsync(
                request.Wer, request.Firma, Verwaltungsakt.Entfernen, cancellationToken);
        }

        var role = await mitgliedschaften.RoleOfAsync(
            request.Mitglied, request.Firma, cancellationToken)
            ?? throw new NotAMemberException();

        if (role == MembershipRole.Admin
            && await mitgliedschaften.CountAdminsAsync(request.Firma, cancellationToken) <= 1)
        {
            throw new LastAdminMayNotLeaveException();
        }

        await mitgliedschaften.RemoveAsync(request.Mitglied, request.Firma, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.MemberRemoved,
                uhr.GetUtcNow(),
                actor: request.Wer,
                tenant: request.Firma,
                target: request.Mitglied,
                correlationId: korrelation.Aktuell),
            cancellationToken);

        return Unit.Value;
    }
}
