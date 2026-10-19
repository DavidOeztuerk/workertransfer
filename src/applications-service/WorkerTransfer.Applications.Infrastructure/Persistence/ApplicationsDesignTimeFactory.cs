using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Builds a context for <c>dotnet ef</c>, which needs the model and never a connection.</summary>
/// <remarks>
/// Here and not in the Api project: a migration belongs in the layer that owns
/// the schema, and creating one should not require the service to be
/// configured.
/// </remarks>
public sealed class ApplicationsDesignTimeFactory
    : IDesignTimeDbContextFactory<ApplicationsDbContext>
{
    /// <inheritdoc />
    public ApplicationsDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ApplicationsDbContext>)ApplicationsDbContextFactory
            .ForDesignTime(
                new DbContextOptionsBuilder<ApplicationsDbContext>(),
                "Host=design-time;Database=applications")
            .Options);
}
