using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Builds a context for <c>dotnet ef</c>, which needs the model and never a connection.</summary>
/// <remarks>
/// Here rather than in the API project: a migration belongs to the layer that
/// owns the schema, and generating one should not require the service to be
/// configured.
/// </remarks>
public sealed class ResumeDesignTimeFactory : IDesignTimeDbContextFactory<ResumeDbContext>
{
    /// <inheritdoc />
    public ResumeDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ResumeDbContext>)ResumeDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<ResumeDbContext>(),
                "Host=entwurfszeit;Database=resume")
            .Options);
}
