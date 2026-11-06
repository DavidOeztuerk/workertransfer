using System.Reflection;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>A real Postgres carrying the real <c>scripts/initdb</c> (ADR-0058).</summary>
/// <remarks>
/// No skip when Docker is missing: <see cref="DatabaseRoleTests"/> is the boss of
/// Q-0002, and a boss that skips itself looks exactly like one that passed.
/// </remarks>
public abstract class RolePostgres : IAsyncLifetime
{
    /// <summary>The bootstrap superuser, as compose and the chart name it.</summary>
    public const string Superuser = "worker";

    /// <summary>What the image demands on an empty volume, and what initdb then ends.</summary>
    public const string BootstrapPassword = "only-until-initdb-ends";

    /// <summary>The script as the image and <c>make db-roles</c> reach it.</summary>
    public const string Script = "01-service-roles.sh";

    private readonly Lazy<PostgreSqlContainer> _container;

    protected RolePostgres() => _container = new(Build);

    /// <summary>The fifteen databases, by the folders under <c>src/</c>.</summary>
    public static IReadOnlyList<string> Databases => RepositoryRoot.Services();

    /// <summary>The running container.</summary>
    public PostgreSqlContainer Container => _container.Value;

    /// <summary>Where the script folder is mounted.</summary>
    protected abstract string ScriptFolder { get; }

    /// <summary>The script's path inside the container.</summary>
    public string ScriptPath => $"{ScriptFolder}/{Script}";

    /// <summary>A service's password in this environment.</summary>
    public virtual string PasswordOf(string database) => $"password-of-{database}-service";

    /// <summary>A service's role.</summary>
    public static string RoleOf(string database) => $"svc_{database}";

    /// <summary>Something beside the plain server, for one fixture.</summary>
    protected virtual PostgreSqlBuilder Adjust(PostgreSqlBuilder builder) => builder;

    public virtual async Task InitializeAsync() => await Container.StartAsync();

    public async Task DisposeAsync() => await Container.DisposeAsync();

    /// <summary>A connection string for one role onto one database.</summary>
    public string ConnectionString(string role, string password, string database) =>
        new NpgsqlConnectionStringBuilder(Container.GetConnectionString())
        {
            Username = role,
            Password = password,
            Database = database,
            Pooling = false,
        }.ConnectionString;

    /// <summary>A service's own connection string.</summary>
    public string ServiceConnection(string database) =>
        ConnectionString(RoleOf(database), PasswordOf(database), database);

    /// <summary>A question to the server as the superuser, over its socket.</summary>
    public async Task<string> AsSuperuser(string database, string statement)
    {
        var result = await Container.ExecAsync(
            ["psql", "-U", Superuser, "-d", database, "-XAtq", "-v", "ON_ERROR_STOP=1", "-c", statement]);

        result.ExitCode.Should().Be(0, result.Stderr);

        return result.Stdout.Trim();
    }

    /// <summary>The script, the way <c>make db-roles</c> calls it: as root, no <c>PGUSER</c>.</summary>
    public Task<ExecResult> RunScript() => Container.ExecAsync([ScriptPath]);

    private PostgreSqlContainer Build()
    {
        var builder = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("identity")
            .WithUsername(Superuser)
            .WithPassword(BootstrapPassword)
            .WithBindMount(
                Path.Combine(RepositoryRoot.Find(), "scripts", "initdb"),
                ScriptFolder,
                AccessMode.ReadOnly);

        foreach (var database in Databases)
        {
            builder = builder.WithEnvironment(
                $"WORKERTRANSFER_DB_PASSWORD_{database.ToUpperInvariant()}", PasswordOf(database));
        }

        return Adjust(builder).Build();
    }
}

/// <summary>An empty volume: the image's entrypoint runs the script itself.</summary>
public sealed class FreshVolume : RolePostgres
{
    protected override string ScriptFolder => "/docker-entrypoint-initdb.d";
}

/// <summary>A volume from before ADR-0058: everything belongs to the superuser.</summary>
/// <remarks>
/// The script lies beside the entrypoint's folder, so it runs only when a test
/// says so — and everything that is here to be moved came about the way it did
/// on a real volume, through the services' real migrations as <c>worker</c>.
/// </remarks>
public class ExistingVolume : RolePostgres
{
    protected override string ScriptFolder => "/workertransfer-initdb";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        foreach (var database in Databases.Where(name => name != "identity"))
        {
            await AsSuperuser("postgres", $"CREATE DATABASE {database} OWNER {Superuser}");
        }

        foreach (var (database, context) in ServiceContexts.All)
        {
            await Migrate(context, ConnectionString(Superuser, BootstrapPassword, database));

            await AsSuperuser(
                database,
                "CREATE TABLE dataprotection_keys (name text PRIMARY KEY, content text NOT NULL, "
                + "created_at timestamptz NOT NULL DEFAULT now());"
                + "INSERT INTO dataprotection_keys (name, content) VALUES ('old', '<key/>');");
        }
    }

    /// <summary>Runs a service's real migrations, as its composition root does.</summary>
    private static async Task Migrate(Type context, string connectionString)
    {
        var factory = context.Assembly.GetType($"{context.FullName}Factory")
            ?? throw new InvalidOperationException($"{context.Name} has no factory.");

        var dataSource = (NpgsqlDataSource)factory
            .GetMethod("DataSource", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [connectionString])!;

        await using (dataSource)
        {
            var builder = (DbContextOptionsBuilder)Activator.CreateInstance(
                typeof(DbContextOptionsBuilder<>).MakeGenericType(context))!;

            factory
                .GetMethod(
                    "Configure",
                    BindingFlags.Public | BindingFlags.Static,
                    [typeof(DbContextOptionsBuilder), typeof(NpgsqlDataSource)])!
                .Invoke(null, [builder, dataSource]);

            await using var instance = (DbContext)Activator.CreateInstance(context, builder.Options)!;
            await instance.Database.MigrateAsync();
        }
    }
}

/// <summary>A server that refuses short passwords, so that an <c>ALTER ROLE</c> can fail.</summary>
public sealed class RefusingVolume : ExistingVolume
{
    /// <summary>Shorter than <c>passwordcheck</c> allows.</summary>
    public const string ShortPassword = "abc1234";

    public override string PasswordOf(string database) =>
        database == "consent" ? ShortPassword : base.PasswordOf(database);

    protected override PostgreSqlBuilder Adjust(PostgreSqlBuilder builder) =>
        builder.WithCommand("-c", "shared_preload_libraries=passwordcheck");

    public override async Task InitializeAsync()
    {
        await Container.StartAsync();

        foreach (var database in Databases.Where(name => name != "identity"))
        {
            await AsSuperuser("postgres", $"CREATE DATABASE {database} OWNER {Superuser}");
        }
    }
}

/// <summary>Which context belongs to which database.</summary>
internal static class ServiceContexts
{
    public static IReadOnlyDictionary<string, Type> All { get; } = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["identity"] = typeof(Identity.Infrastructure.Persistence.IdentityDbContext),
        ["consent"] = typeof(Consent.Infrastructure.Persistence.ConsentDbContext),
        ["profile"] = typeof(Profile.Infrastructure.Persistence.ProfileDbContext),
        ["resume"] = typeof(Resume.Infrastructure.Persistence.ResumeDbContext),
        ["portfolio"] = typeof(Portfolio.Infrastructure.Persistence.PortfolioDbContext),
        ["jobs"] = typeof(Jobs.Infrastructure.Persistence.JobsDbContext),
        ["applications"] = typeof(Applications.Infrastructure.Persistence.ApplicationsDbContext),
        ["companies"] = typeof(Companies.Infrastructure.Persistence.CompaniesDbContext),
        ["transfer"] = typeof(Transfer.Infrastructure.Persistence.TransferDbContext),
        ["github"] = typeof(GitHub.Infrastructure.Persistence.GitHubDbContext),
        ["notification"] = typeof(Notification.Infrastructure.Persistence.NotificationDbContext),
        ["scout"] = typeof(Scout.Infrastructure.Persistence.ScoutDbContext),
        ["advisor"] = typeof(Advisor.Infrastructure.Persistence.AdvisorDbContext),
        ["assessment"] = typeof(Assessment.Infrastructure.Persistence.AssessmentDbContext),
        ["messages"] = typeof(Messages.Infrastructure.Persistence.MessagesDbContext),
    };
}
