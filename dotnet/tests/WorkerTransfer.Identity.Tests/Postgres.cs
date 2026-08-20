using System.Diagnostics;
using Testcontainers.PostgreSql;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// A Postgres carrying the schema Alembic really produces.
/// </summary>
/// <remarks>
/// The migrations are run by the Python service itself rather than rebuilt
/// here: this suite exists to catch a mapping that disagrees with the actual
/// table, and a hand-written schema would only ever agree with the mapping.
/// <para>
/// Fails rather than skips when no container runtime is reachable. A silently
/// skipped integration suite looks exactly like a passing one, and what it
/// covers here — column names, a Postgres enum, citext, jsonb — is invisible
/// until it is wrong.
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
        Migriere();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private void Migriere()
    {
        var wurzel = Repowurzel();
        var url = $"postgresql+asyncpg://worker:worker@{_container.Hostname}:"
                  + $"{_container.GetMappedPublicPort(5432)}/identity";

        var start = new ProcessStartInfo("uv")
        {
            WorkingDirectory = Path.Combine(wurzel, "apps", "identity-service"),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("alembic");
        start.ArgumentList.Add("upgrade");
        start.ArgumentList.Add("head");
        start.Environment["WORKER_DATABASE_URL"] = url;

        using var prozess = Process.Start(start)
            ?? throw new InvalidOperationException("uv konnte nicht gestartet werden.");

        var ausgabe = prozess.StandardOutput.ReadToEnd();
        var fehler = prozess.StandardError.ReadToEnd();
        prozess.WaitForExit();

        if (prozess.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"alembic upgrade head endete mit {prozess.ExitCode}.\n{ausgabe}\n{fehler}");
        }
    }

    private static string Repowurzel()
    {
        var verzeichnis = new DirectoryInfo(AppContext.BaseDirectory);

        while (verzeichnis is not null && !File.Exists(Path.Combine(verzeichnis.FullName, "CLAUDE.md")))
        {
            verzeichnis = verzeichnis.Parent;
        }

        return verzeichnis?.FullName
            ?? throw new InvalidOperationException("Die Repowurzel wurde nicht gefunden.");
    }
}

/// <summary>One container for every test that needs the schema.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<Postgres>
{
    public const string Name = "postgres";
}
