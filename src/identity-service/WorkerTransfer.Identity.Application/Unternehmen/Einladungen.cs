using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Companies;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Invite an address into a company.</summary>
/// <remarks>
/// An address, not an account. The invited person need not have one — and the
/// answer is the same either way, or the endpoint would be a way to ask about
/// platform membership without asking the consent ledger.
/// </remarks>
public sealed record InviteMemberCommand(
    SubjectId Wer, TenantId Firma, string Email, MembershipRole Rolle) : IBefehl<Invitation>;

/// <inheritdoc cref="MitgliedEinladenBefehl" />
public sealed class InviteMemberHandler(
    CompanyAccess zugriff,
    IInvitationRepository invitations,
    ICompanyRepository companies,
    IUserRepository benutzer,
    IEinmaltoken einmaltoken,
    IPostkorb postkorb,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<InviteMemberCommand, Invitation>
{
    /// <inheritdoc />
    public async Task<Invitation> Handle(
        InviteMemberCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await zugriff.AsAdminAsync(
            request.Wer, request.Firma, Verwaltungsakt.Einladen, cancellationToken);

        var invitation = Invitation.Issue(
            request.Firma, request.Email.Trim().ToLowerInvariant(), request.Rolle,
            role, request.Wer, uhr.GetUtcNow());

        var (klartext, hash) = einmaltoken.Erzeuge();
        await invitations.AddAsync(invitation, hash, cancellationToken);

        var company = await companies.FindByIdAsync(request.Firma, cancellationToken);

        // Die Kontosprache der EINLADENDEN, und das ist eine bewusste Notlösung:
        // die eingeladene Adresse hat hier womöglich gar kein Konto — genau
        // dafür ist die Einladung da. Es gibt also keine Zeile von ihr, die man
        // lesen könnte. Wer eine Kollegin einlädt, schreibt ihr am ehesten in
        // der Kontosprache, in der er selbst arbeitet.
        var einladenderKonto = await benutzer.FindByIdAsync(
            request.Wer, cancellationToken);

        postkorb.Einladung(
            invitation.Email,
            request.Wer,
            company?.Name ?? string.Empty,
            klartext,
            einladenderKonto?.Kontosprache ?? Sprachwahl.Vorgabe);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.MemberInvited,
                uhr.GetUtcNow(),
                actor: request.Wer,
                tenant: request.Firma,
                correlationId: korrelation.Aktuell,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    // The role is technical metadata about a permission, not a
                    // statement about the person — and the address is not on the
                    // allowlist for exactly that reason.
                    ["role"] = MembershipRoleNames.ToDatabase(request.Rolle)
                }),
            cancellationToken);

        return invitation;
    }
}

/// <summary>Take an open invitation back.</summary>
public sealed record RevokeInvitationCommand(SubjectId Wer, TenantId Firma, Guid Einladung)
    : IBefehl<Unit>;

/// <inheritdoc cref="EinladungZuruecknehmenBefehl" />
public sealed class RevokeInvitationHandler(
    CompanyAccess zugriff,
    IInvitationRepository invitations,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<RevokeInvitationCommand, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(
        RevokeInvitationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var role = await zugriff.AsAdminAsync(
            request.Wer, request.Firma, Verwaltungsakt.Einladen, cancellationToken);

        // Looked up inside the company, never by id alone: otherwise an
        // administrator of one company could act on another company's
        // invitation.
        var invitation = await invitations.FindAsync(
            request.Firma, request.Einladung, cancellationToken)
            ?? throw new InvitationInvalidException();

        invitation.Withdraw(role);
        await invitations.SaveAsync(invitation, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.InvitationWithdrawn,
                uhr.GetUtcNow(),
                actor: request.Wer,
                tenant: request.Firma,
                correlationId: korrelation.Aktuell),
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
public sealed record AcceptInvitationCommand(SubjectId Wer, string Token)
    : IBefehl<Membership>;

/// <inheritdoc cref="EinladungAnnehmenBefehl" />
public sealed class AcceptInvitationHandler(
    IInvitationRepository invitations,
    IUserRepository benutzer,
    IMembershipRepository mitgliedschaften,
    ICompanyRepository companies,
    IEinmaltoken einmaltoken,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<AcceptInvitationCommand, Membership>
{
    /// <inheritdoc />
    public async Task<Membership> Handle(
        AcceptInvitationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invitation = await invitations.FindByTokenHashAsync(
            einmaltoken.Hashe(request.Token), cancellationToken)
            ?? throw new InvitationInvalidException();

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken)
            ?? throw new InvitationInvalidException();

        invitation.Accept(konto.Email, uhr.GetUtcNow());
        await invitations.SaveAsync(invitation, cancellationToken);

        await mitgliedschaften.AddAsync(
            request.Wer, invitation.Tenant, invitation.Role, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.MemberJoined,
                uhr.GetUtcNow(),
                actor: request.Wer,
                tenant: invitation.Tenant,
                correlationId: korrelation.Aktuell,
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
