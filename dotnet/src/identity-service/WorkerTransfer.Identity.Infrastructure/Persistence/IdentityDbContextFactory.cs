using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>
/// Builds the data source and the context that read the existing schema.
/// </summary>
public static class IdentityDbContextFactory
{
    /// <summary>
    /// A data source that reads <c>account_status</c> as the name it stores.
    /// </summary>
    /// <remarks>
    /// Unmapped rather than mapped to the CLR enum: the translation already
    /// exists in <c>AccountStatusNames</c>, is pinned by a test, and is the
    /// same one the domain uses. A second one configured here would be a second
    /// place for the four names to drift.
    /// </remarks>
    public static NpgsqlDataSource DataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.EnableUnmappedTypes();
        return builder.Build();
    }

    /// <summary>A context over <paramref name="connectionString"/>.</summary>
    public static IdentityDbContext Fuer(string connectionString) =>
        new(Optionen(DataSource(connectionString)).Options);

    /// <summary>Options for a data source the caller owns.</summary>
    public static DbContextOptionsBuilder<IdentityDbContext> Optionen(NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<IdentityDbContext>)Konfiguriere(
            new DbContextOptionsBuilder<IdentityDbContext>(), dataSource);

    /// <summary>
    /// The one place tracking is switched off, so the container and the tests
    /// cannot end up with different behaviour.
    /// </summary>
    /// <param name="options">The builder to configure.</param>
    /// <param name="dataSource">Where the data lives.</param>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }
}
