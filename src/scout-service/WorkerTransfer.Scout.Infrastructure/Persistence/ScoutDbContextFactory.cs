using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Scout.Infrastructure.Persistence;

/// <summary>Baut Datenquelle und Kontext.</summary>
public static class ScoutDbContextFactory
{
    /// <summary>Die Datenquelle.</summary>
    public static NpgsqlDataSource Datenquelle(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>
    /// Die eine Stelle, an der Verfolgen abgeschaltet wird, damit Behälter und
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
public sealed class ScoutDesignTimeFactory : IDesignTimeDbContextFactory<ScoutDbContext>
{
    /// <inheritdoc />
    public ScoutDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ScoutDbContext>)ScoutDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<ScoutDbContext>(),
                "Host=entwurfszeit;Database=scout")
            .Options);
}
