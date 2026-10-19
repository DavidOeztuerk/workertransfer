using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>A real Postgres carrying this service's schema.</summary>
/// <remarks>
/// <strong>Until the migration Alembic ran here</strong> — the Python service
/// owned the base schema, and this suite really drove its migrations, so a
/// mapping is not checked against a hand-written table it agrees with anyway.
/// <para>
/// With the move identity-service owns its schema itself, like the other ten:
/// one EF migration creates all nine tables, both Postgres enumerations and
/// <c>citext</c>. What the suite checks stays the same — column names, labels,
/// types — only the source of the schema has changed.
/// </para>
/// <para>
/// Fails rather than skipping itself when no container runtime is reachable. A
/// silently skipped integration suite looks exactly like a passing one.
/// </para>
/// </remarks>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("identity")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    /// <summary>Npgsql connection string for the migrated database.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var source = IdentityDbContextFactory.DataSource(ConnectionString);
        await using var context = new IdentityDbContext(
            (DbContextOptions<IdentityDbContext>)IdentityDbContextFactory.Configure(
                new DbContextOptionsBuilder<IdentityDbContext>(), source).Options);

        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Where this repository begins, found by walking up.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root was not found.");
    }
}

/// <summary>One container for every test that needs the schema.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}
