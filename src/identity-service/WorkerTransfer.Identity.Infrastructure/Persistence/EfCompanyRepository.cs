using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Companies;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads and writes the <c>tenants</c> table.</summary>
public sealed class EfCompanyRepository(IdentityDbContext context, TimeProvider uhr)
    : ICompanyRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// <c>tenants.domain</c> is citext and unique, so the database decides
    /// whether two spellings are one domain. Comparing here would let
    /// <c>Firma.de</c> claim a domain <c>firma.de</c> already holds.
    /// </remarks>
    public async Task<Company?> FindByDomainAsync(
        EmailDomain domain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domain);

        var row = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Domain == domain.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task<Company?> FindByIdAsync(
        TenantId id,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Tenants
            .FirstOrDefaultAsync(candidate => candidate.Id == id.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public Task AddAsync(Company company, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(company);

        context.Tenants.Add(new TenantRow
        {
            Id = company.Id.Value,
            Name = company.Name,
            Domain = company.Domain.Value,
            Status = company.Status == TenantStatus.Dormant ? "dormant" : "active",
            CreatedAt = uhr.GetUtcNow().UtcDateTime
        });

        return Task.CompletedTask;
    }

    private static Company ZumAggregat(TenantRow row) => Company.Restore(
        new TenantId(row.Id),
        row.Name,
        new EmailDomain(row.Domain),
        row.Status == "dormant" ? TenantStatus.Dormant : TenantStatus.Active);
}
