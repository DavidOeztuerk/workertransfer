using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>Builds the data source and the context.</summary>
public static class AdvisorDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>
    /// The one place tracking is switched off, so that containers and tests do
    /// not end up with different behaviour.
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
public sealed class AdvisorDesignTimeFactory : IDesignTimeDbContextFactory<AdvisorDbContext>
{
    /// <inheritdoc />
    public AdvisorDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<AdvisorDbContext>)AdvisorDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<AdvisorDbContext>(),
                "Host=design-time;Database=advisor")
            .Options);
}
