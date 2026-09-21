using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Builds the data source and the context over this service's schema.</summary>
public static class GitHubDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Options over a data source that belongs to the caller.</summary>
    public static DbContextOptionsBuilder<GitHubDbContext> Options(
        NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<GitHubDbContext>)Configure(
            new DbContextOptionsBuilder<GitHubDbContext>(), dataSource);

    /// <summary>
    /// Die eine Job, an der die Nachverfolgung ausgeschaltet wird.
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

    /// <summary>The same model for <c>dotnet ef</c>.</summary>
    public static DbContextOptionsBuilder AtDesignTime(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Builds a context for <c>dotnet ef</c>.</summary>
public sealed class GitHubDesignTimeFactory : IDesignTimeDbContextFactory<GitHubDbContext>
{
    /// <inheritdoc />
    public GitHubDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<GitHubDbContext>)GitHubDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<GitHubDbContext>(),
                "Host=entwurfszeit;Database=github")
            .Options);
}
