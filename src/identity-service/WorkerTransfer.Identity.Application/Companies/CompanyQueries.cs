using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Companies;

/// <summary>Which companies may I act for?</summary>
public sealed record MyCompaniesQuery(SubjectId Subject) : IQueryMessage<IReadOnlyList<Membership>>;

/// <inheritdoc cref="MyCompaniesQuery" />
public sealed class MyCompaniesHandler(IMembershipRepository memberships)
    : IRequestHandler<MyCompaniesQuery, IReadOnlyList<Membership>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Membership>> Handle(
        MyCompaniesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return memberships.ListForSubjectAsync(request.Subject, cancellationToken);
    }
}

/// <summary>Who may act for this company?</summary>
public sealed record MembersQuery(SubjectId Subject, TenantId Company)
    : IQueryMessage<IReadOnlyList<CompanyMember>>;

/// <inheritdoc cref="MembersQuery" />
/// <remarks>
/// Any member may read the list — it is the company's own team, and hiding
/// colleagues from colleagues serves nobody. Only membership is required, and
/// that check is what keeps it from being a directory of strangers.
/// </remarks>
public sealed class MembersHandler(
    CompanyAccess access,
    IMembershipRepository memberships)
    : IRequestHandler<MembersQuery, IReadOnlyList<CompanyMember>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CompanyMember>> Handle(
        MembersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await access.RoleAsync(request.Subject, request.Company, cancellationToken);

        return await memberships.ListMembersAsync(request.Company, cancellationToken);
    }
}

/// <summary>A company's members — for service to service, without a caller.</summary>
/// <remarks>
/// The secret at the internal endpoint IS the authorisation. The public query
/// checks membership because it answers a human being; here a service answers a
/// service, and <c>SubjectId.Empty</c> as a stand-in would throw
/// <c>NotAMemberException</c>.
/// </remarks>
public sealed record InternalMembersQuery(TenantId Company)
    : IQueryMessage<IReadOnlyList<CompanyMember>>;

/// <inheritdoc cref="InternalMembersQuery" />
public sealed class InternalMembersHandler(IMembershipRepository memberships)
    : IRequestHandler<InternalMembersQuery, IReadOnlyList<CompanyMember>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<CompanyMember>> Handle(
        InternalMembersQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return memberships.ListMembersAsync(request.Company, cancellationToken);
    }
}

/// <summary>Which invitations are still waiting?</summary>
public sealed record OpenInvitationsQuery(SubjectId Subject, TenantId Company)
    : IQueryMessage<IReadOnlyList<Invitation>>;

/// <inheritdoc cref="OpenInvitationsQuery" />
public sealed class OpenInvitationsHandler(
    CompanyAccess access,
    IInvitationRepository invitations)
    : IRequestHandler<OpenInvitationsQuery, IReadOnlyList<Invitation>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Invitation>> Handle(
        OpenInvitationsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        await access.RoleAsync(request.Subject, request.Company, cancellationToken);

        return await invitations.ListOpenAsync(request.Company, cancellationToken);
    }
}

/// <summary>What role does this human being have in this company?</summary>
/// <remarks>
/// <para>For service to service, without a caller: the secret at the internal
/// endpoint IS the authorisation. The public queries check the asker's
/// membership first, because they answer a human being — here a service answers
/// a service.</para>
///
/// <para><strong>Why the other ten services have to ask at all.</strong> The
/// membership table lives here and nowhere else (ADR-0004: no shared database).
/// And the role does not belong in the token: a token lives on after somebody
/// was removed from a company, and the removal would then take effect only on
/// expiry. The price is one query per protected request — it hits a primary key
/// and touches only the operations that bind a company.</para>
/// </remarks>
public sealed record InternalRoleQuery(TenantId Company, SubjectId Subject)
    : IQueryMessage<MembershipRole?>;

/// <inheritdoc cref="InternalRoleQuery" />
public sealed class InternalRoleHandler(IMembershipRepository memberships)
    : IRequestHandler<InternalRoleQuery, MembershipRole?>
{
    /// <inheritdoc />
    public Task<MembershipRole?> Handle(
        InternalRoleQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return memberships.RoleOfAsync(request.Subject, request.Company, cancellationToken);
    }
}

/// <summary>Who a company is — name and proven domain.</summary>
/// <remarks>
/// <para>For service to service, without a caller: the secret at the internal
/// endpoint IS the authorisation.</para>
///
/// <para><strong>Why advisor-service needs this.</strong> A person may exclude
/// companies, and they name them by their domain (ADR-0037). Without this query
/// advisor-service would have to hold a second domain table — and that would be
/// the one that goes stale first, with an exclusion that then silently stops
/// biting.</para>
///
/// <para>A company's domain is <strong>no information about a human
/// being</strong>: it stands on every careers page. What does not go out here
/// is the workforce — the members query is there for that, and it is a door of
/// its own.</para>
/// </remarks>
public sealed record InternalCompanyQuery(TenantId Company) : IQueryMessage<Company?>;

/// <inheritdoc cref="InternalCompanyQuery" />
public sealed class InternalCompanyHandler(ICompanyRepository companies)
    : IRequestHandler<InternalCompanyQuery, Company?>
{
    /// <inheritdoc />
    public Task<Company?> Handle(
        InternalCompanyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return companies.FindByIdAsync(request.Company, cancellationToken);
    }
}
