using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Portfolio.Infrastructure.Persistence;

/// <summary>Builds the data source and the context.</summary>
public static class PortfolioDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>
    /// The one place where tracking is switched off, so that container and
    /// tests do not end up with different behaviour.
    /// </summary>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options, NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>, which needs no connection.</summary>
    public static DbContextOptionsBuilder AtDesignTime(
        DbContextOptionsBuilder options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>The context for <c>dotnet ef</c>.</summary>
public sealed class PortfolioDesignTimeFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    /// <inheritdoc />
    public PortfolioDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<PortfolioDbContext>)PortfolioDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<PortfolioDbContext>(),
                "Host=entwurfszeit;Database=portfolio")
            .Options);
}
