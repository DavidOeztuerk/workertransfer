using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>Baut Datenquelle und Kontext.</summary>
public static class PortfolioDbContextFactory
{
    /// <summary>Die Datenquelle.</summary>
    public static NpgsqlDataSource Datenquelle(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>
    /// Die eine Stelle, an der Verfolgen abgeschaltet wird, damit Container und
    /// Tests nicht mit verschiedenem Verhalten enden.
    /// </summary>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>, das keine Verbindung braucht.</summary>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Der Kontext für <c>dotnet ef</c>.</summary>
public sealed class PortfolioDesignTimeFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    /// <inheritdoc />
    public PortfolioDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<PortfolioDbContext>)PortfolioDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<PortfolioDbContext>(),
                "Host=entwurfszeit;Database=portfolio")
            .Options);
}
