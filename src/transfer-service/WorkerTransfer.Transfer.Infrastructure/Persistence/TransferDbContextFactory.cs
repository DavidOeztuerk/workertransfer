using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Transfer.Infrastructure.Persistence;

/// <summary>Builds the data source and the context over this service's schema.</summary>
/// <remarks>
/// Without <c>MapEnum</c>: the states stand as words in <c>varchar</c> columns,
/// exactly as they do in the contract and in the interface.
/// </remarks>
public static class TransferDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Options over a data source that belongs to the caller.</summary>
    public static DbContextOptionsBuilder<TransferDbContext> Options(
        NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<TransferDbContext>)Configure(
            new DbContextOptionsBuilder<TransferDbContext>(), dataSource);

    /// <summary>
    /// The one place where tracking is switched off, so that
    /// container, tests and <c>dotnet ef</c> do not end up with different
    /// models.
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
    public static DbContextOptionsBuilder AtDesignTime(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Builds a context for <c>dotnet ef</c>.</summary>
public sealed class TransferDesignTimeFactory : IDesignTimeDbContextFactory<TransferDbContext>
{
    /// <inheritdoc />
    public TransferDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<TransferDbContext>)TransferDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<TransferDbContext>(),
                "Host=design-time;Database=transfer")
            .Options);
}
