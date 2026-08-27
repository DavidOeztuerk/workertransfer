using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>Builds a context for <c>dotnet ef</c>, which needs the model and no connection.</summary>
/// <remarks>
/// Here rather than in the API project: a migration belongs to the layer that
/// owns the schema, and generating one should not require the service to be
/// configured.
/// </remarks>
public sealed class ConsentDesignTimeFactory : IDesignTimeDbContextFactory<ConsentDbContext>
{
    /// <inheritdoc />
    public ConsentDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ConsentDbContext>)ConsentDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<ConsentDbContext>(),
                "Host=entwurfszeit;Database=consent")
            .Options);
}
