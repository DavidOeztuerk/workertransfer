using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Baut Datenquelle und Kontext über dem Schema dieses Dienstes.</summary>
/// <remarks>
/// Ohne <c>MapEnum</c> — dieser Dienst legt keinen Postgres-Enumtyp an. Der
/// Stand steht als Wort in einer <c>varchar</c>-Spalte, genau so, wie er im
/// Vertrag und in der Oberfläche steht.
/// </remarks>
public static class ApplicationsDbContextFactory
{
    /// <summary>Die Datenquelle.</summary>
    public static NpgsqlDataSource Datenquelle(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Optionen über einer Datenquelle, die dem Aufrufer gehört.</summary>
    public static DbContextOptionsBuilder<ApplicationsDbContext> Optionen(
        NpgsqlDataSource datenquelle) =>
        (DbContextOptionsBuilder<ApplicationsDbContext>)Konfiguriere(
            new DbContextOptionsBuilder<ApplicationsDbContext>(), datenquelle);

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
