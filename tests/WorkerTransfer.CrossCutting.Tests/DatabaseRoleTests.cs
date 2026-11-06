using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>One service, one role, one database (ADR-0058).</summary>
/// <remarks>
/// The chain this breaks: a service taken over logged in as the superuser, read
/// the encrypted AI keys out of <c>identity</c> and opened them with the master
/// key from its own environment. Both halves are checked against the real
/// script on a real server, because a role alone proves nothing: <c>CONNECT</c>
/// stands open to <c>PUBLIC</c> by default, and a fresh role walked straight
/// into <c>identity</c>.
/// </remarks>
public sealed class DatabaseRoleTests(FreshVolume volume) : IClassFixture<FreshVolume>
{
    /// <summary>The boss of Q-0002.</summary>
    [Fact]
    public Task CrossServiceDbAccess_Denied() => RoleMatrix.Holds(volume);

    /// <summary>The superuser has no way in over the network.</summary>
    /// <remarks>
    /// <c>POSTGRES_PASSWORD</c> works only on an empty volume and is ended by the
    /// script, so the value compose handed over opens nothing either.
    /// </remarks>
    [Theory]
    [InlineData(RolePostgres.Superuser)]
    [InlineData(RolePostgres.BootstrapPassword)]
    [InlineData("")]
    public async Task The_superuser_has_no_network_login(string password)
    {
        await using var connection = new NpgsqlConnection(
            volume.ConnectionString(RolePostgres.Superuser, password, "postgres"));

        var opening = () => connection.OpenAsync();

        await opening.Should().ThrowAsync<Exception>("worker logs in over its socket and nowhere else");
    }

    /// <summary>The probe compose asks, in the state nobody had run before.</summary>
    /// <remarks>
    /// It runs over <c>127.0.0.1</c>, which the image trusts: without that the
    /// healthcheck would go red the moment the password is gone, and the stack
    /// would never come up.
    /// </remarks>
    [Fact]
    public async Task The_healthcheck_runs_without_a_password()
    {
        var probe = await volume.Container.ExecAsync(
            ["psql", "-h", "127.0.0.1", "-U", RolePostgres.Superuser, "-d", "postgres", "-c", "select 1"]);

        probe.ExitCode.Should().Be(0, probe.Stderr);
    }

    /// <summary>Everything the services do at startup, a plain owner can do.</summary>
    /// <remarks>
    /// The test hosts run their Postgres as superuser and would not see it. The
    /// whole proof is the stack coming up in CI; this is the part a suite can
    /// hold: <c>citext</c> is the one statement of the migrations that reads as
    /// if it needed a superuser.
    /// </remarks>
    [Fact]
    public async Task The_owner_can_create_what_a_migration_creates()
    {
        await using var connection = new NpgsqlConnection(volume.ServiceConnection("portfolio"));
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "CREATE EXTENSION IF NOT EXISTS citext; CREATE TABLE probe (id citext PRIMARY KEY); DROP TABLE probe;",
            connection);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The list in the script is the one compose starts.</summary>
    /// <remarks>
    /// Two lists, because the script is mounted alone into a container and can
    /// read nothing beside it. A service that stands only in compose spins in
    /// "password authentication failed" on the next fresh volume.
    /// </remarks>
    [Fact]
    public void The_script_names_exactly_the_databases_of_compose()
    {
        var root = RepositoryRoot.Find();
        var script = File.ReadAllText(Path.Combine(root, "scripts", "initdb", RolePostgres.Script));
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));

        var named = Regex.Match(
                script, "^databases=\"(?<names>[a-z ]+)\"", RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .Groups["names"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var started = Regex.Matches(
                compose, @"^\s+ConnectionStrings__(?<name>[a-z]+): Host=", RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .Select(hit => hit.Groups["name"].Value);

        named.Should().BeEquivalentTo(started);
        named.Should().BeEquivalentTo(RolePostgres.Databases);
    }

    /// <summary>What the entrypoint needs of the file to run and not to source it.</summary>
    /// <remarks>
    /// Sourced, an <c>exit</c> would end the entrypoint and with it the setup.
    /// </remarks>
    [Fact]
    public void The_script_is_executable_and_never_exits()
    {
        var path = Path.Combine(RepositoryRoot.Find(), "scripts", "initdb", RolePostgres.Script);

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).Should().HaveFlag(UnixFileMode.UserExecute);
        }

        var code = File.ReadAllLines(path).Where(row => !row.TrimStart().StartsWith('#'));

        code.Should().NotContain(row => Regex.IsMatch(row, @"\bexit\b", RegexOptions.None, TimeSpan.FromSeconds(5)));
    }
}

/// <summary>What must hold of the fifteen roles, on any volume.</summary>
internal static class RoleMatrix
{
    private const string InsufficientPrivilege = "42501";

    public static async Task Holds(RolePostgres postgres)
    {
        var unreachable = RolePostgres.Databases.Concat(["postgres", "template1"]).ToList();

        foreach (var database in RolePostgres.Databases)
        {
            var role = RolePostgres.RoleOf(database);

            await using (var own = new NpgsqlConnection(postgres.ServiceConnection(database)))
            {
                await own.OpenAsync();
            }

            foreach (var foreign in unreachable.Where(name => name != database))
            {
                await using var connection = new NpgsqlConnection(
                    postgres.ConnectionString(role, postgres.PasswordOf(database), foreign));

                var opening = () => connection.OpenAsync();

                (await opening.Should().ThrowAsync<PostgresException>($"{role} must not enter {foreign}"))
                    .Which.SqlState.Should().Be(InsufficientPrivilege, $"{role} on {foreign}");
            }

            (await postgres.AsSuperuser(
                    "postgres",
                    "SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication OR rolbypassrls "
                    + $"FROM pg_roles WHERE rolname = '{role}'"))
                .Should().Be("f", $"{role} carries none of the role attributes");

            (await postgres.AsSuperuser(
                    "postgres",
                    "SELECT count(*) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.member "
                    + $"WHERE r.rolname = '{role}'"))
                .Should().Be("0", $"{role} is a member of no other role");
        }
    }
}

/// <summary>A volume that ran before ADR-0058 moves over and keeps its data.</summary>
public sealed class ExistingVolumeTests(ExistingVolume volume) : IClassFixture<ExistingVolume>
{
    /// <summary>What still belongs to the superuser, extensions and their dependants aside.</summary>
    private const string LeftOver = """
        SELECT count(*) FROM (
          SELECT c.oid, 'pg_class'::regclass AS catalog FROM pg_class c
           WHERE c.oid >= 16384 AND pg_get_userbyid(c.relowner) = current_user
          UNION ALL
          SELECT t.oid, 'pg_type'::regclass FROM pg_type t
           WHERE t.oid >= 16384 AND pg_get_userbyid(t.typowner) = current_user
          UNION ALL
          SELECT p.oid, 'pg_proc'::regclass FROM pg_proc p
           WHERE p.oid >= 16384 AND pg_get_userbyid(p.proowner) = current_user
        ) object
        WHERE NOT EXISTS (
          SELECT FROM pg_depend d
           WHERE d.classid = object.catalog AND d.objid = object.oid AND d.deptype IN ('e', 'i'))
        """;

    /// <summary>Row counts of every table but the ring, as one text.</summary>
    private async Task<string> Rows(string database)
    {
        var tables = (await volume.AsSuperuser(
                database,
                "SELECT quote_ident(tablename) FROM pg_tables WHERE schemaname = 'public' "
                + "AND tablename <> 'dataprotection_keys' ORDER BY tablename"))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        tables.Should().NotBeEmpty($"{database} carries its real migrations");

        var counted = string.Join(
            " UNION ALL ",
            tables.Select(table => $"SELECT '{table}', count(*) FROM {table}"));

        return await volume.AsSuperuser(database, counted);
    }

    [Fact]
    public async Task An_existing_volume_moves_over_and_keeps_its_data()
    {
        // A row of a real table: the one thing here a person would miss.
        await volume.AsSuperuser(
            "portfolio",
            "INSERT INTO portfolios (id, items, created_at, updated_at) "
            + "VALUES (gen_random_uuid(), '[]', now(), now())");

        var before = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var database in RolePostgres.Databases)
        {
            before[database] = await Rows(database);
        }

        var first = await volume.RunScript();
        first.ExitCode.Should().Be(0, first.Stderr);

        foreach (var database in RolePostgres.Databases)
        {
            (await volume.AsSuperuser("postgres", $"SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = '{database}'"))
                .Should().Be(RolePostgres.RoleOf(database));

            (await volume.AsSuperuser(database, LeftOver))
                .Should().Be("0", $"nothing in {database} may stay with the superuser");

            (await Rows(database)).Should().Be(before[database], $"the rows of {database} stay");

            (await volume.AsSuperuser(database, "SELECT count(*) FROM dataprotection_keys"))
                .Should().Be("0", "entries under the old key would fail at every start");

            await using var connection = new NpgsqlConnection(volume.ServiceConnection(database));
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                "INSERT INTO dataprotection_keys (name, content) VALUES ('new', '<key/>')", connection);
            await insert.ExecuteNonQueryAsync();
        }

        var second = await volume.RunScript();
        second.ExitCode.Should().Be(0, second.Stderr);

        foreach (var database in RolePostgres.Databases)
        {
            (await volume.AsSuperuser(database, "SELECT count(*) FROM dataprotection_keys"))
                .Should().Be("1", "the ring is emptied once, and what the service wrote afterwards stays");
        }

        await using (var service = new NpgsqlConnection(volume.ServiceConnection("portfolio")))
        {
            await service.OpenAsync();
            await using var read = new NpgsqlCommand("SELECT count(*) FROM portfolios", service);

            (await read.ExecuteScalarAsync()).Should().Be(1L, "the service reads what it owned before");
        }

        await RoleMatrix.Holds(volume);
    }
}

/// <summary>A run that breaks halfway leaves the database as it found it.</summary>
public sealed class InterruptedRunTests(ExistingVolume volume) : IClassFixture<ExistingVolume>
{
    /// <summary>Lets the script get as far as the functions and fall there.</summary>
    private const string Trap = """
        CREATE FUNCTION interrupted_run_target() RETURNS integer LANGUAGE sql AS 'SELECT 1';
        CREATE FUNCTION interrupted_run_trap() RETURNS event_trigger LANGUAGE plpgsql AS
          $$ BEGIN RAISE EXCEPTION 'interrupted on purpose'; END $$;
        CREATE EVENT TRIGGER interrupted_run ON ddl_command_start WHEN TAG IN ('ALTER ROUTINE')
          EXECUTE FUNCTION interrupted_run_trap();
        """;

    [Fact]
    public async Task A_run_that_breaks_changes_nothing_and_the_next_one_goes_through()
    {
        await volume.AsSuperuser("portfolio", Trap);

        var broken = await volume.RunScript();

        broken.ExitCode.Should().NotBe(0, "otherwise this test interrupted nothing");
        broken.Stderr.Should().Contain("interrupted on purpose");

        (await volume.AsSuperuser("postgres", "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'portfolio'"))
            .Should().Be(RolePostgres.Superuser);

        (await volume.AsSuperuser("portfolio", "SELECT pg_get_userbyid(relowner) FROM pg_class WHERE relname = 'dataprotection_keys'"))
            .Should().Be(RolePostgres.Superuser, "the tables were moved first, and the fall takes them back");

        (await volume.AsSuperuser("portfolio", "SELECT count(*) FROM dataprotection_keys"))
            .Should().Be("1", "the ring is emptied in the same transaction");

        await volume.AsSuperuser(
            "portfolio",
            "DROP EVENT TRIGGER interrupted_run; DROP FUNCTION interrupted_run_trap(); DROP FUNCTION interrupted_run_target();");

        var next = await volume.RunScript();
        next.ExitCode.Should().Be(0, next.Stderr);

        (await volume.AsSuperuser("postgres", "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'portfolio'"))
            .Should().Be(RolePostgres.RoleOf("portfolio"));
    }
}

/// <summary>What the script does not know how to move, it refuses to leave behind.</summary>
/// <remarks>
/// A database that changes hands with a leftover would look finished and keep an
/// object nobody can use: the service cannot touch it, and the superuser no
/// longer goes there. A volume of its own, because a run moves every database it
/// reaches before it falls.
/// </remarks>
public sealed class LeftBehindObjectTests(ExistingVolume volume) : IClassFixture<ExistingVolume>
{
    [Fact]
    public async Task An_object_it_cannot_move_stops_the_database_from_changing_hands()
    {
        await volume.AsSuperuser("scout", "CREATE TEXT SEARCH DICTIONARY left_behind (TEMPLATE = simple)");

        var run = await volume.RunScript();

        run.ExitCode.Should().NotBe(0);
        run.Stderr.Should().Contain("left_behind");

        (await volume.AsSuperuser("postgres", "SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = 'scout'"))
            .Should().Be(RolePostgres.Superuser);

        await volume.AsSuperuser("scout", "DROP TEXT SEARCH DICTIONARY left_behind");
    }
}

/// <summary>A statement that fails must not write its password into the server log.</summary>
/// <remarks>
/// <c>log_min_error_statement</c> defaults to <c>error</c>, so a failing
/// <c>ALTER ROLE</c> lands in the log with its text. Logs travel further than a
/// database.
/// </remarks>
public sealed class PasswordLogTests(RefusingVolume volume) : IClassFixture<RefusingVolume>
{
    [Fact]
    public async Task A_failing_statement_leaves_no_password_in_the_server_log()
    {
        var run = await volume.RunScript();

        run.ExitCode.Should().NotBe(0, "the short password has to fail, or nothing is measured here");

        var (standardOutput, standardError) = await volume.Container.GetLogsAsync();
        var log = standardOutput + standardError;

        log.Should().Contain("password is too short", "otherwise the log was not read");
        log.Should().NotContain(RefusingVolume.ShortPassword);
        run.Stdout.Should().NotContain(RefusingVolume.ShortPassword);
        run.Stderr.Should().NotContain(RefusingVolume.ShortPassword);
    }
}
