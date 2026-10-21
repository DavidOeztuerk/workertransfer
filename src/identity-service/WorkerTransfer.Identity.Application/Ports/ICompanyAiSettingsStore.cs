using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Reads and writes which model writes for a company.</summary>
public interface ICompanyAiSettingsStore
{
    /// <summary>The settings — or the default, when nothing was ever set.</summary>
    /// <param name="company">Whose.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>Never <c>null</c>: "nothing set" is a state, not an absence.</returns>
    Task<CompanyAiSettings> GetAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Writes them back.</summary>
    /// <param name="settings">What holds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task SaveAsync(
        CompanyAiSettings settings, CancellationToken cancellationToken = default);
}
