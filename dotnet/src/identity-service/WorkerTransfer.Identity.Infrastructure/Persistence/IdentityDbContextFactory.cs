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
        new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
}
