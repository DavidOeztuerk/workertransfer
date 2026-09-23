using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace WorkerTransfer.Companies.Infrastructure.Persistence;

/// <summary>Builds the data source and the context over this service's schema.</summary>
/// <remarks>
/// Without <c>MapEnum</c>: this service creates no Postgres enum type.
/// </remarks>
public static class CompaniesDbContextFactory
{
    /// <summary>The data source.</summary>
    public static NpgsqlDataSource DataSource(string connectionString) =>
        new NpgsqlDataSourceBuilder(connectionString).Build();

    /// <summary>Options over a DataSource that belongs to the caller.</summary>
    public static DbContextOptionsBuilder<CompaniesDbContext> Options(
        NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<CompaniesDbContext>)Configure(
            new DbContextOptionsBuilder<CompaniesDbContext>(), dataSource);

    /// <summary>
    /// The one place where tracking is switched off, so that container,
    /// tests and <c>dotnet ef</c> do not end up with different models.
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
/// <remarks>
/// Here and not in the Api project: a migration belongs in the layer that owns
/// the schema.
/// </remarks>
public sealed class CompaniesDesignTimeFactory : IDesignTimeDbContextFactory<CompaniesDbContext>
{
    /// <inheritdoc />
    public CompaniesDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<CompaniesDbContext>)CompaniesDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<CompaniesDbContext>(),
                "Host=entwurfszeit;Database=companies")
            .Options);
}
