using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>Finds accounts.</summary>
/// <remarks>
/// Returns detached aggregates. A change to one reaches the database only
/// through an explicit save, which is the semantics the Python service has and
/// the reason tracking is off — see "Change Tracking" in
/// <c>docs/MIGRATION-PROMPT.md</c>.
/// </remarks>
public interface IUserRepository
{
    /// <param name="email">The address, in any casing.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The account, or <c>null</c> when there is none.</returns>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <param name="id">Who to look up.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The account, or <c>null</c> when there is none.</returns>
    Task<User?> FindByIdAsync(SubjectId id, CancellationToken cancellationToken = default);
}
