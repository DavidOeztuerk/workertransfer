using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The exact set of labels behind <c>audit_events.action</c>.
/// </summary>
/// <remarks>
/// The migration creates the type only when no type of that name is there. That
/// guard is right — it holds in both worlds and needs no memory of which one it
/// is in — but it asks about the name and not about the contents. A database
/// whose labels differ from what this service writes passes the guard and fails
/// on the first insert, which may be the first invitation ever withdrawn.
/// <para>
/// So the set is pinned here three ways: written out, against the real column,
/// and against the guard's own SQL.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class PruefspurEtikettenTests(Postgres postgres)
{
    /// <summary>
    /// The labels, in the order the type declares them. Written out rather than
    /// derived: they are a contract with rows Python already wrote, and a rule
    /// that produces them today produces something else the day a member is
    /// renamed — silently, and only for new rows.
    /// </summary>
    private static readonly string[] Etiketten =
    [
        "register",
        "login_success",
        "login_failure",
        "token_refresh",
        "token_revoke",
        "tenant_switch",
        "tenant_switch_denied",
        "email_verified",
        "company_created",
        "member_invited",
        "member_joined",
        "invitation_withdrawn",
        "member_removed"
    ];

    [Fact]
    public void Es_gibt_genau_ein_Etikett_je_Mitglied()
    {
        Etiketten.Should().HaveCount(Enum.GetValues<AuditAction>().Length);
        Etiketten.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// Against the type Alembic really created. A label this service knows and
    /// the column does not is an insert that fails; the other way round is a row
    /// Python can write and this service cannot read.
    /// </summary>
    [Fact]
    public async Task Die_Spalte_kennt_genau_diese_Etiketten()
    {
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = """
            SELECT e.enumlabel
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            WHERE t.typname = 'audit_action'
            ORDER BY e.enumsortorder
            """;

        var gefunden = new List<string>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            gefunden.Add(leser.GetString(0));
        }

        gefunden.Should().BeEquivalentTo(Etiketten);
    }

    /// <summary>
    /// And against the guard, which is what creates the type once the Python
    /// service is gone. A guard that creates a different set than the one this
    /// suite pins would simply move the failure to the first fresh database.
    /// </summary>
    [Fact]
    public void Der_Guard_in_der_Migration_legt_genau_diese_Etiketten_an()
    {
        var migration = Path.Combine(
            Postgres.Repowurzel(),
            "dotnet", "src", "identity-service",
            "WorkerTransfer.Identity.Infrastructure", "Persistence", "Migrations");

        var datei = Directory.EnumerateFiles(migration, "*_HandlungsformDerSitzung.cs").Single();
        var text = File.ReadAllText(datei);

        var block = Regex.Match(
            text, @"CREATE TYPE audit_action AS ENUM \((?<werte>[^)]*)\)", RegexOptions.None,
            TimeSpan.FromSeconds(5));

        block.Success.Should().BeTrue("die Migration muss den Typ noch anlegen können");

        var gefunden = Regex.Matches(
                block.Groups["werte"].Value, "'(?<name>[a-z_]+)'", RegexOptions.None,
                TimeSpan.FromSeconds(5))
            .Select(treffer => treffer.Groups["name"].Value)
            .ToArray();

        gefunden.Should().Equal(Etiketten);
    }
}
