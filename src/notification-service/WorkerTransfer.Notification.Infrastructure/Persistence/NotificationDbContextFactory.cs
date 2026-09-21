using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Baut DataSource und Kontext über dem Schema dieses Dienstes.</summary>
public static class NotificationDbContextFactory
{
    /// <summary>Die DataSource.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Optionen über einer DataSource, die dem Aufrufer gehört.</summary>
    public static DbContextOptionsBuilder<NotificationDbContext> Optionen(
        NpgsqlDataSource datenquelle) =>
        (DbContextOptionsBuilder<NotificationDbContext>)Configure(
            new DbContextOptionsBuilder<NotificationDbContext>(), datenquelle);

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
public sealed class NotificationDesignTimeFactory
    : IDesignTimeDbContextFactory<NotificationDbContext>
{
    /// <inheritdoc />
    public NotificationDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<NotificationDbContext>)NotificationDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<NotificationDbContext>(),
                "Host=entwurfszeit;Database=notification")
            .Options);
}
