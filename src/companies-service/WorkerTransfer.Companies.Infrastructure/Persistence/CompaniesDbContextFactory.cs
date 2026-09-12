using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Baut Datenquelle und Kontext über dem Schema dieses Dienstes.</summary>
/// <remarks>
/// Ohne <c>MapEnum</c>: dieser Dienst legt keinen Postgres-Enumtyp an.
/// </remarks>
public static class CompaniesDbContextFactory
{
    /// <summary>Die Datenquelle.</summary>
    public static NpgsqlDataSource Datenquelle(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Optionen über einer Datenquelle, die dem Aufrufer gehört.</summary>
    public static DbContextOptionsBuilder<CompaniesDbContext> Optionen(
        NpgsqlDataSource datenquelle) =>
        (DbContextOptionsBuilder<CompaniesDbContext>)Konfiguriere(
            new DbContextOptionsBuilder<CompaniesDbContext>(), datenquelle);

    /// <summary>
    /// Die eine Stelle, an der die Nachverfolgung ausgeschaltet wird, damit
    /// Behälter, Tests und <c>dotnet ef</c> nicht mit verschiedenen Modellen
    /// enden.
    /// </summary>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource datenquelle)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(datenquelle)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>, das keine Verbindung braucht.</summary>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Baut einen Kontext für <c>dotnet ef</c>.</summary>
/// <remarks>
/// Hier und nicht im Api-Projekt: eine Wanderung gehört in die Schicht, der das
/// Schema gehört.
/// </remarks>
public sealed class CompaniesDesignTimeFactory : IDesignTimeDbContextFactory<CompaniesDbContext>
{
    /// <inheritdoc />
    public CompaniesDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<CompaniesDbContext>)CompaniesDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<CompaniesDbContext>(),
                "Host=entwurfszeit;Database=companies")
            .Options);
}
