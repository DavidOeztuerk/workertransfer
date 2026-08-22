using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Stores invitations.</summary>
public interface IInvitationRepository
{
    /// <summary>Records a new invitation and the hash of its token.</summary>
    /// <remarks>
    /// Only the hash. The plaintext goes out by mail and stands nowhere in the
    /// database — the same rule as the confirmation link.
    /// </remarks>
    Task AddAsync(
        Invitation invitation,
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>The invitation a token names, if any.</summary>
    Task<Invitation?> FindByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>The invitation with this id inside this company, if any.</summary>
    /// <remarks>
    /// Scoped by company on purpose: an id alone would let an administrator of
    /// one company act on another company's invitation.
    /// </remarks>
    Task<Invitation?> FindAsync(
        TenantId tenant,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Everything still awaiting an answer in this company.</summary>
    Task<IReadOnlyList<Invitation>> ListOpenAsync(
        TenantId tenant,
        CancellationToken cancellationToken = default);

    /// <summary>Writes back what changed on one invitation.</summary>
    Task SaveAsync(Invitation invitation, CancellationToken cancellationToken = default);
}
