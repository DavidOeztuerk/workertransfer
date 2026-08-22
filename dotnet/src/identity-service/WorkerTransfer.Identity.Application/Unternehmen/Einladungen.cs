using Girder.Core.Identity;
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
public sealed record MitgliedEinladenBefehl(
    SubjectId Wer, TenantId Firma, string Email, MembershipRole Rolle) : IBefehl<Invitation>;

/// <inheritdoc cref="MitgliedEinladenBefehl" />
public sealed class MitgliedEinladenHandler(
    Firmenzugriff zugriff,
    IInvitationRepository einladungen,
    ICompanyRepository firmen,
    IEinmaltoken einmaltoken,
    IPostkorb postkorb,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<MitgliedEinladenBefehl, Invitation>
{
    /// <inheritdoc />
    public async Task<Invitation> Handle(
        MitgliedEinladenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rolle = await zugriff.AlsAdminAsync(
            request.Wer, request.Firma, Verwaltungsakt.Einladen, cancellationToken);

        var einladung = Invitation.Issue(
            request.Firma, request.Email.Trim().ToLowerInvariant(), request.Rolle,
            rolle, request.Wer, uhr.GetUtcNow());

        var (klartext, hash) = einmaltoken.Erzeuge();
        await einladungen.AddAsync(einladung, hash, cancellationToken);

        var firma = await firmen.FindByIdAsync(request.Firma, cancellationToken);

        postkorb.Einladung(einladung.Email, request.Wer, firma?.Name ?? string.Empty, klartext);

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

        return einladung;
    }
}

/// <summary>Take an open invitation back.</summary>
public sealed record EinladungZuruecknehmenBefehl(SubjectId Wer, TenantId Firma, Guid Einladung)
    : IBefehl<Unit>;

/// <inheritdoc cref="EinladungZuruecknehmenBefehl" />
public sealed class EinladungZuruecknehmenHandler(
    Firmenzugriff zugriff,
    IInvitationRepository einladungen,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<EinladungZuruecknehmenBefehl, Unit>
{
    /// <inheritdoc />
    public async Task<Unit> Handle(
        EinladungZuruecknehmenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var rolle = await zugriff.AlsAdminAsync(
            request.Wer, request.Firma, Verwaltungsakt.Einladen, cancellationToken);

        // Looked up inside the company, never by id alone: otherwise an
        // administrator of one company could act on another company's
        // invitation.
        var einladung = await einladungen.FindAsync(
            request.Firma, request.Einladung, cancellationToken)
            ?? throw new InvitationInvalidException();

        einladung.Withdraw(rolle);
        await einladungen.SaveAsync(einladung, cancellationToken);

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
public sealed record EinladungAnnehmenBefehl(SubjectId Wer, string Token)
    : IBefehl<Mitgliedschaft>;

/// <inheritdoc cref="EinladungAnnehmenBefehl" />
public sealed class EinladungAnnehmenHandler(
    IInvitationRepository einladungen,
    IUserRepository benutzer,
    IMembershipRepository mitgliedschaften,
    ICompanyRepository firmen,
    IEinmaltoken einmaltoken,
    IAuditTrail protokoll,
    IKorrelation korrelation,
    TimeProvider uhr)
    : IRequestHandler<EinladungAnnehmenBefehl, Mitgliedschaft>
{
    /// <inheritdoc />
    public async Task<Mitgliedschaft> Handle(
        EinladungAnnehmenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var einladung = await einladungen.FindByTokenHashAsync(
            einmaltoken.Hashe(request.Token), cancellationToken)
            ?? throw new InvitationInvalidException();

        var konto = await benutzer.FindByIdAsync(request.Wer, cancellationToken)
            ?? throw new InvitationInvalidException();

        einladung.Accept(konto.Email, uhr.GetUtcNow());
        await einladungen.SaveAsync(einladung, cancellationToken);

        await mitgliedschaften.AddAsync(
            request.Wer, einladung.Tenant, einladung.Role, cancellationToken);

        await protokoll.AppendAsync(
            new AuditEvent(
                AuditAction.MemberJoined,
                uhr.GetUtcNow(),
                actor: request.Wer,
                tenant: einladung.Tenant,
                correlationId: korrelation.Aktuell,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["role"] = MembershipRoleNames.ToDatabase(einladung.Role)
                }),
            cancellationToken);

        var firma = await firmen.FindByIdAsync(einladung.Tenant, cancellationToken);

        // Joining changes the memberships, not the running session: to act for
        // the new company one switches deliberately afterwards (ADR-0018). An
        // automatic switch would push somebody out of the company they are
        // working in, unasked.
        return new Mitgliedschaft(
            einladung.Tenant,
            firma?.Name ?? string.Empty,
            firma?.Domain.Value ?? string.Empty,
            einladung.Role);
    }
}
