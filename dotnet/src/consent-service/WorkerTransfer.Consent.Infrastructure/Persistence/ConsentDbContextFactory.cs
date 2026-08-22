using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>Builds the data source and the context over it.</summary>
public static class ConsentDbContextFactory
{
    /// <summary>A data source for the consent database.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>
    /// The one place tracking is switched off, so the container, the tests and
    /// <c>dotnet ef</c> cannot end up with different models.
    /// </summary>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>, which needs no connection.</summary>
    /// <remarks>
    /// Must configure exactly what <see cref="Konfiguriere"/> configures. A
    /// design-time model that differs from the running one produces migrations
    /// for changes nobody made — and, worse, none for changes somebody did.
    /// </remarks>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}
