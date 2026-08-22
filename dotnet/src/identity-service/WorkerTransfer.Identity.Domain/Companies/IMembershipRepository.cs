using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>What somebody may do inside a company.</summary>
/// <remarks>
/// Not in the token, ever. A token says which company somebody acts for, never
/// with what rights — the role is read from the membership per operation, so
/// withdrawing it takes effect on the next read rather than on the next
/// sign-in.
/// </remarks>
public enum MembershipRole
{
    /// <summary>May invite and remove.</summary>
    Admin,

    /// <summary>May act for the company.</summary>
    Member
}

/// <summary>Maps <see cref="MembershipRole"/> to the stored value.</summary>
public static class MembershipRoleNames
{
    /// <summary>The stored value for a role.</summary>
    public static string ToDatabase(MembershipRole role) => role switch
    {
        MembershipRole.Admin => "admin",
        MembershipRole.Member => "member",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.")
    };

    /// <summary>The role for a stored value.</summary>
    public static MembershipRole FromDatabase(string stored) => stored switch
    {
        "admin" => MembershipRole.Admin,
        "member" => MembershipRole.Member,
        _ => throw new ArgumentOutOfRangeException(nameof(stored), stored, "Unknown role.")
    };
}

/// <summary>One company a person may act for, as a picker needs it.</summary>
/// <param name="Tenant">Which company.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Domain">The domain it was proven on.</param>
/// <param name="Role">With what rights, read from the relation.</param>
public sealed record Mitgliedschaft(TenantId Tenant, string Name, string Domain, MembershipRole Role);

/// <summary>One member of a company, as a list needs them.</summary>
public sealed record Firmenmitglied(SubjectId Subject, string DisplayName, MembershipRole Role);

/// <summary>Which companies a person may act for.</summary>
/// <remarks>
/// A relation, never a column on the account: one person may act for several
/// companies, and none is more theirs than the others (ADR-0018).
/// </remarks>
public interface IMembershipRepository
{
    /// <summary>Whether this person may currently act for this company.</summary>
    /// <remarks>
    /// Asked afresh every time a token is minted, including on every refresh.
    /// A membership checked once and then carried in a claim is a membership
    /// that was true once — and whoever was let in would stay in for as long as
    /// they keep refreshing, long after the company removed them.
    /// </remarks>
    /// <param name="subject">Who is asking.</param>
    /// <param name="tenant">The company they name.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    Task<bool> IsMemberAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default);

    /// <summary>Lets somebody act for a company from now on.</summary>
    Task AddAsync(
        SubjectId subject,
        TenantId tenant,
        MembershipRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What this person may do inside this company, or <c>null</c> if nothing.
    /// </summary>
    /// <remarks>
    /// Read here and never from a claim. A token says which company somebody
    /// acts for, never with what rights — so withdrawing a role takes effect on
    /// the next operation rather than on the next sign-in.
    /// </remarks>
    Task<MembershipRole?> RoleOfAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default);

    /// <summary>Every company this person may act for.</summary>
    Task<IReadOnlyList<Mitgliedschaft>> ListForSubjectAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default);

    /// <summary>Everybody who may act for this company.</summary>
    Task<IReadOnlyList<Firmenmitglied>> ListMembersAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default);

    /// <summary>How many administrators this company has.</summary>
    /// <remarks>
    /// Asked before anybody leaves. One is the floor, not a preference: a
    /// company without an administrator cannot let anyone in again.
    /// </remarks>
    Task<int> CountAdminsAsync(TenantId tenant, CancellationToken cancellationToken = default);

    /// <summary>Ends one membership.</summary>
    Task RemoveAsync(
        SubjectId subject,
        TenantId tenant,
        CancellationToken cancellationToken = default);
}
