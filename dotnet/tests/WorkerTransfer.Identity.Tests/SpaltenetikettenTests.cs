using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The exact set of labels behind each Postgres enum this service writes.
/// </summary>
/// <remarks>
/// The migration creates a type only when no type of that name is there. That
/// guard is right — it holds in both worlds and needs no memory of which one it
/// is in — but it asks about the name and not about the contents. A database
/// whose labels differ from what this service writes passes the guard and fails
/// on the first insert, which for <c>invitation_withdrawn</c> may be the first
/// invitation ever withdrawn, months later.
/// <para>
/// So each set is pinned three ways: written out, against the real column, and
/// against the guard's own SQL. The labels are also a contract with rows Python
/// already wrote — a rule that derives them today derives something else the
/// day a member is renamed, silently and only for new rows.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class SpaltenetikettenTests(Postgres postgres)
{
    private static readonly string[] Pruefspur =
    [
        "register", "login_success", "login_failure", "token_refresh", "token_revoke",
        "tenant_switch", "tenant_switch_denied", "email_verified", "company_created",
        "member_invited", "member_joined", "invitation_withdrawn", "member_removed"
    ];

    private static readonly string[] Kontostand =
    [
        "pending", "active", "suspended", "disabled"
    ];

    public static TheoryData<string, string[], int> Aufzaehlungen => new()
    {
        { "audit_action", Pruefspur, 13 },
        { "account_status", Kontostand, 4 }
    };

    [Theory]
    [MemberData(nameof(Aufzaehlungen))]
    public void Es_gibt_genau_ein_Etikett_je_Mitglied(
        string typ, string[] etiketten, int mitglieder)
    {
        typ.Should().NotBeEmpty();
        etiketten.Should().HaveCount(mitglieder);
        etiketten.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Die_Aufzaehlungen_haben_so_viele_Mitglieder_wie_angegeben()
    {
        Enum.GetValues<AuditAction>().Should().HaveCount(Pruefspur.Length);
        Enum.GetValues<AccountStatus>().Should().HaveCount(Kontostand.Length);
    }

    /// <summary>
    /// Against the type Alembic really created. A label this service knows and
    /// the column does not is an insert that fails; the other way round is a
    /// row Python can write and this service cannot read.
    /// </summary>
    [Theory]
    [MemberData(nameof(Aufzaehlungen))]
    public async Task Die_Spalte_kennt_genau_diese_Etiketten(
        string typ, string[] etiketten, int mitglieder)
    {
        mitglieder.Should().BePositive();

        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();
        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = """
            SELECT e.enumlabel
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            WHERE t.typname = @typ
            ORDER BY e.enumsortorder
            """;
        befehl.Parameters.AddWithValue("typ", typ);

        var gefunden = new List<string>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            gefunden.Add(leser.GetString(0));
        }

        gefunden.Should().BeEquivalentTo(etiketten);
    }

    /// <summary>
    /// And against the guard, which is what creates the type once the Python
    /// service is gone. A guard that creates a different set than the one this
    /// suite pins would simply move the failure to the first fresh database.
    /// </summary>
    [Theory]
    [MemberData(nameof(Aufzaehlungen))]
    public void Ein_Guard_in_den_Migrationen_legt_genau_diese_Etiketten_an(
        string typ, string[] etiketten, int mitglieder)
    {
        mitglieder.Should().BePositive();

        var ordner = Path.Combine(
            Postgres.Repowurzel(),
            "dotnet", "src", "identity-service",
            "WorkerTransfer.Identity.Infrastructure", "Persistence", "Migrations");

        var block = Directory.EnumerateFiles(ordner, "*.cs")
            .Select(File.ReadAllText)
            .Select(text => Regex.Match(
                text, $@"CREATE TYPE {typ} AS ENUM \((?<werte>[^)]*)\)",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            .SingleOrDefault(treffer => treffer.Success);

        block.Should().NotBeNull($"genau eine Migration muss {typ} noch anlegen können");

        var gefunden = Regex.Matches(
                block!.Groups["werte"].Value, "'(?<name>[a-z_]+)'",
                RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(treffer => treffer.Groups["name"].Value)
            .ToArray();

        gefunden.Should().Equal(etiketten);
    }
}
