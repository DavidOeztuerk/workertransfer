using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Companies;

/// <summary>Finds and stores companies.</summary>
public interface ICompanyRepository
{
    /// <summary>The company that claimed a domain, if any.</summary>
    Task<Company?> FindByDomainAsync(
        EmailDomain domain,
        CancellationToken cancellationToken = default);

    /// <summary>The company with this id, if any.</summary>
    Task<Company?> FindByIdAsync(TenantId id, CancellationToken cancellationToken = default);

    /// <summary>Records a new company.</summary>
    Task AddAsync(Company company, CancellationToken cancellationToken = default);
}
