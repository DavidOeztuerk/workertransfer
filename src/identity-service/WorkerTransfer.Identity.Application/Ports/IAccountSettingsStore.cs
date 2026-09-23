using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Reads and writes what a person decided about themselves.</summary>
public interface IAccountSettingsStore
{
    /// <summary>The settings — or the default, when nothing was ever set.</summary>
    /// <param name="subject">Whose.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Never <c>null</c>: "nothing set" is a state, not an absence.</returns>
    Task<AccountSettings> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Writes them back.</summary>
    /// <param name="settings">What holds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveAsync(
        AccountSettings settings, CancellationToken cancellationToken = default);
}
