using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Baut DataSource und Kontext über dem Schema dieses Dienstes.</summary>
public static class GitHubDbContextFactory
{
    /// <summary>Die DataSource.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Optionen über einer DataSource, die dem Aufrufer gehört.</summary>
    public static DbContextOptionsBuilder<GitHubDbContext> Optionen(
        NpgsqlDataSource datenquelle) =>
        (DbContextOptionsBuilder<GitHubDbContext>)Configure(
            new DbContextOptionsBuilder<GitHubDbContext>(), datenquelle);

    /// <summary>
    /// Die eine Job, an der die Nachverfolgung ausgeschaltet wird.
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

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>.</summary>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString);
    }
}

/// <summary>Baut einen Kontext für <c>dotnet ef</c>.</summary>
public sealed class GitHubDesignTimeFactory : IDesignTimeDbContextFactory<GitHubDbContext>
{
    /// <inheritdoc />
    public GitHubDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<GitHubDbContext>)GitHubDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<GitHubDbContext>(),
                "Host=entwurfszeit;Database=github")
            .Options);
}
