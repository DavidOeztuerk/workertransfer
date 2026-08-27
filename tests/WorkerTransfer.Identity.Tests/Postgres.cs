using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>Ein echtes Postgres mit dem Schema dieses Dienstes.</summary>
/// <remarks>
/// <strong>Bis zur Migration lief hier Alembic</strong> — der Python-Dienst
/// besass das Grundschema, und diese Reihe fuhr seine Wanderungen wirklich
/// aus, damit eine Abbildung nicht gegen eine handgeschriebene Tabelle
/// geprueft wird, der sie ohnehin zustimmt.
/// <para>
/// Mit dem Umzug besitzt identity-service sein Schema selbst, wie die zehn
/// anderen: eine EF-Wanderung legt alle neun Tabellen an, beide
/// Postgres-Aufzaehlungen und <c>citext</c>. Was die Reihe prueft, bleibt
/// dasselbe — Spaltennamen, Etiketten, Typen —, nur die Quelle des Schemas
/// hat gewechselt.
/// </para>
/// <para>
/// Faellt aus, statt sich zu ueberspringen, wenn keine Behaelterlaufzeit
/// erreichbar ist. Eine still uebersprungene Integrationsreihe sieht genauso
/// aus wie eine bestandene.
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

    /// <summary>Npgsql-Verbindungszeichenfolge zur gewanderten Datenbank.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var quelle = IdentityDbContextFactory.DataSource(ConnectionString);
        await using var kontext = new IdentityDbContext(
            (DbContextOptions<IdentityDbContext>)IdentityDbContextFactory.Konfiguriere(
                new DbContextOptionsBuilder<IdentityDbContext>(), quelle).Options);

        await kontext.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Wo dieses Repository beginnt, durch Hochlaufen gefunden.</summary>
    public static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null
               && !File.Exists(Path.Combine(verzeichnis.FullName, "CLAUDE.md")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
            ?? throw new InvalidOperationException("Die Repowurzel wurde nicht gefunden.");
    }
}

/// <summary>Ein Behaelter fuer jeden Test, der das Schema braucht.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}
