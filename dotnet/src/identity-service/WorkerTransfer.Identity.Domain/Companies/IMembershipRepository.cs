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
}
