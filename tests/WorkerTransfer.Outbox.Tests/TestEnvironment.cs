using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace WorkerTransfer.Outbox.Tests;

/// <summary>A context that holds nothing but the outbox.</summary>
public sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ConfigureOutbox();
    }
}

/// <summary>Hands out contexts, the way the dispatcher asks for them.</summary>
public sealed class ContextFactory(string connection) : IDbContextFactory<ProbeContext>
{
    public ProbeContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ProbeContext>()
            .UseNpgsql(connection)
            // As the services configure it. With tracking on by default
            // this suite could not see a dispatcher that writes onto an
            // untracked copy — which is exactly the bug it missed once.
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options);
}

/// <summary>A real Postgres. <c>FOR UPDATE SKIP LOCKED</c> has no fake.</summary>
public sealed class Postgres : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("outbox")
        .WithUsername("worker")
        .WithPassword("worker")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = new ContextFactory(ConnectionString).CreateDbContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}
