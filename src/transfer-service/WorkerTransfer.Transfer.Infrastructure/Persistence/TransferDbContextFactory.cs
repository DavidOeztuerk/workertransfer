using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Baut DataSource und Kontext über dem Schema dieses Dienstes.</summary>
/// <remarks>
/// Ohne <c>MapEnum</c>: die drei Stände stehen als Worte in
/// <c>varchar</c>-Spalten, genau so, wie sie im Vertrag und in der Oberfläche
/// stehen.
/// </remarks>
public static class TransferDbContextFactory
{
    /// <summary>Die DataSource.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Optionen über einer DataSource, die dem Aufrufer gehört.</summary>
    public static DbContextOptionsBuilder<TransferDbContext> Optionen(
        NpgsqlDataSource datenquelle) =>
        (DbContextOptionsBuilder<TransferDbContext>)Configure(
            new DbContextOptionsBuilder<TransferDbContext>(), datenquelle);

    /// <summary>
    /// Die eine Job, an der die Nachverfolgung ausgeschaltet wird, damit
    /// Behälter, Tests und <c>dotnet ef</c> nicht mit verschiedenen Modellen
    /// enden.
    /// </summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        NpgsqlDataSource datenquelle)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(datenquelle)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>, das keine Connection braucht.</summary>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Baut einen Kontext für <c>dotnet ef</c>.</summary>
public sealed class TransferDesignTimeFactory : IDesignTimeDbContextFactory<TransferDbContext>
{
    /// <inheritdoc />
    public TransferDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<TransferDbContext>)TransferDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<TransferDbContext>(),
                "Host=entwurfszeit;Database=transfer")
            .Options);
}
