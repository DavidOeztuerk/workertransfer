using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

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
}
