using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Unternehmen;

/// <summary>Which companies may I act for?</summary>
public sealed record MyCompaniesQuery(SubjectId Wer) : IAbfrage<IReadOnlyList<Membership>>;

/// <inheritdoc cref="MeineUnternehmenAbfrage" />
public sealed class MyCompaniesHandler(IMembershipRepository mitgliedschaften)
    : IRequestHandler<MyCompaniesQuery, IReadOnlyList<Membership>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Membership>> Handle(
        MyCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return mitgliedschaften.ListForSubjectAsync(request.Wer, cancellationToken);
    }
}

/// <summary>Who may act for this company?</summary>
public sealed record MembersQuery(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<CompanyMember>>;

/// <inheritdoc cref="MitgliederAbfrage" />
/// <remarks>
/// Any member may read the list — it is the company's own team, and hiding
/// colleagues from colleagues serves nobody. Only membership is required, and
/// that check is what keeps it from being a directory of strangers.
/// </remarks>
public sealed class MembersHandler(
    CompanyAccess zugriff,
    IMembershipRepository mitgliedschaften)
    : IRequestHandler<MembersQuery, IReadOnlyList<CompanyMember>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CompanyMember>> Handle(
        MembersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await zugriff.RoleAsync(request.Wer, request.Firma, cancellationToken);

        return await mitgliedschaften.ListMembersAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Die Mitglieder eines Unternehmens — für Dienst-zu-Dienst, ohne Aufrufer.</summary>
/// <remarks>
/// Das Geheimnis am internen Endpunkt IST die Autorisierung. Die öffentliche
/// Abfrage prüft Membership, weil sie einem Menschen antwortet; hier
/// antwortet der Dienst einem Dienst, und <c>SubjectId.Empty</c> als
/// Stellvertreter würde <c>NotAMemberException</c> werfen.
/// </remarks>
public sealed record InternalMembersQuery(TenantId Firma)
    : IAbfrage<IReadOnlyList<CompanyMember>>;

/// <inheritdoc cref="InterneMitgliederAbfrage" />
public sealed class InternalMembersHandler(IMembershipRepository mitgliedschaften)
    : IRequestHandler<InternalMembersQuery, IReadOnlyList<CompanyMember>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CompanyMember>> Handle(
        InternalMembersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return mitgliedschaften.ListMembersAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Which invitations are still waiting?</summary>
public sealed record OpenInvitationsQuery(SubjectId Wer, TenantId Firma)
    : IAbfrage<IReadOnlyList<Invitation>>;

/// <inheritdoc cref="OffeneEinladungenAbfrage" />
public sealed class OpenInvitationsHandler(
    CompanyAccess zugriff,
    IInvitationRepository invitations)
    : IRequestHandler<OpenInvitationsQuery, IReadOnlyList<Invitation>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Invitation>> Handle(
        OpenInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await zugriff.RoleAsync(request.Wer, request.Firma, cancellationToken);

        return await invitations.ListOpenAsync(request.Firma, cancellationToken);
    }
}

/// <summary>Welche Rolle hat dieser Mensch in diesem Unternehmen?</summary>
/// <remarks>
/// <para>Für Dienst-zu-Dienst, ohne Aufrufer: das Geheimnis am internen
/// Endpunkt IST die Autorisierung. Die öffentlichen Abfragen prüfen zuerst die
/// Membership des Fragenden, weil sie einem Menschen antworten — hier
/// antwortet ein Dienst einem Dienst.</para>
///
/// <para><strong>Warum die anderen zehn Dienste überhaupt fragen müssen.</strong>
/// Die Mitgliedschaftstabelle liegt hier und nirgends sonst (ADR-0004: keine
/// gemeinsame Datenbank). Und ins Token gehört die Rolle nicht: ein Token lebt
/// weiter, nachdem jemand aus einer Firma entfernt wurde, und die Entfernung
/// wirkte dann erst beim Ablauf. Der Preis ist eine Abfrage je geschützter
/// ResumeRequest — sie trifft einen Primärschlüssel und betrifft nur die Handlungen,
/// die ein Unternehmen binden.</para>
/// </remarks>
public sealed record InternalRoleQuery(TenantId Firma, SubjectId Wer)
    : IAbfrage<MembershipRole?>;

/// <inheritdoc cref="InterneRolleAbfrage" />
public sealed class InternalRoleHandler(IMembershipRepository mitgliedschaften)
    : IRequestHandler<InternalRoleQuery, MembershipRole?>
{
    /// <inheritdoc />
    public Task<MembershipRole?> Handle(
        InternalRoleQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return mitgliedschaften.RoleOfAsync(request.Wer, request.Firma, cancellationToken);
    }
}

/// <summary>Wer ein Unternehmen ist — Name und bewiesene Domain.</summary>
/// <remarks>
/// <para>Für Dienst-zu-Dienst, ohne Aufrufer: das Geheimnis am internen
/// Endpunkt IST die Autorisierung.</para>
///
/// <para><strong>Warum advisor-service das braucht.</strong> Eine Person darf
/// Unternehmen ausschliessen, und sie benennt sie durch ihre Domain (ADR-0037).
/// Ohne diese Abfrage müsste advisor-service eine zweite Domaintabelle halten —
/// und die wäre die, die als Erste veraltet, mit einem Ausschluss, der dann
/// stillschweigend nicht mehr greift.</para>
///
/// <para>Die Domain eines Unternehmens ist <strong>keine Auskunft über einen
/// Menschen</strong>: sie steht auf jeder Karriereseite. Was hier nicht
/// herausgeht, ist die Belegschaft — dafür gibt es die Mitgliederabfrage, und
/// sie ist eine eigene Tür.</para>
/// </remarks>
public sealed record InternalCompanyQuery(TenantId Firma) : IAbfrage<Company?>;

/// <inheritdoc cref="InterneFirmaAbfrage" />
public sealed class InternalCompanyHandler(ICompanyRepository unternehmen)
    : IRequestHandler<InternalCompanyQuery, Company?>
{
    /// <inheritdoc />
    public Task<Company?> Handle(
        InternalCompanyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return unternehmen.FindByIdAsync(request.Firma, cancellationToken);
    }
}
