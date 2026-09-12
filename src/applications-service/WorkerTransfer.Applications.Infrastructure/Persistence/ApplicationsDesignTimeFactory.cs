using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Baut einen Kontext für <c>dotnet ef</c>, das das Modell braucht und nie eine Verbindung.</summary>
/// <remarks>
/// Hier und nicht im Api-Projekt: eine Wanderung gehört in die Schicht, der das
/// Schema gehört, und eine zu erzeugen soll nicht verlangen, dass der Dienst
/// konfiguriert ist.
/// </remarks>
public sealed class ApplicationsDesignTimeFactory
    : IDesignTimeDbContextFactory<ApplicationsDbContext>
{
    /// <inheritdoc />
    public ApplicationsDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ApplicationsDbContext>)ApplicationsDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<ApplicationsDbContext>(),
                "Host=entwurfszeit;Database=applications")
            .Options);
}
