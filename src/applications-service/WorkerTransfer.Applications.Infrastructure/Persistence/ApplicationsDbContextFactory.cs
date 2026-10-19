using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace WorkerTransfer.Applications.Infrastructure.Persistence;

/// <summary>Builds data source and context over this service's schema.</summary>
/// <remarks>
/// Without <c>MapEnum</c> — this service creates no Postgres enum type. The
/// state stands as a word in a <c>varchar</c> column, exactly as it stands in
/// the contract and in the interface.
/// </remarks>
public static class ApplicationsDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Options over a data source that belongs to the caller.</summary>
    public static DbContextOptionsBuilder<ApplicationsDbContext> OptionsFor(
        NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<ApplicationsDbContext>)Configure(
            new DbContextOptionsBuilder<ApplicationsDbContext>(), dataSource);

    /// <summary>
    /// The one place where tracking is switched off, so container, tests and
    /// <c>dotnet ef</c> do not end up with different models.
    /// </summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>, which needs no connection.</summary>
    public static DbContextOptionsBuilder ForDesignTime(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}
