using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Die genaue Etikettenmenge hinter jeder Postgres-Aufzaehlung, die dieser
/// Dienst schreibt.
/// </summary>
/// <remarks>
/// Solange Alembic das Schema besass, legte eine handgeschriebene SchemaMigration den
/// Typ unter einem Guard an, und dieser Test las dessen SQL. Seit dem Umzug
/// erzeugt EF die Typen selbst — aus der Anmerkung <c>Npgsql:Enum:&lt;typ&gt;</c>
/// in der SchemaMigration. Geprueft wird dasselbe wie vorher: <em>welche</em>
/// Etiketten entstehen.
/// <para>
/// Jede Menge wird dreifach festgenagelt: ausgeschrieben, gegen die echte
/// Spalte und gegen die SchemaMigration. Ein Etikett, das dieser Dienst kennt und die
/// Spalte nicht, ist ein Einfuegen, das scheitert — bei
/// <c>invitation_withdrawn</c> vielleicht erst bei der ersten je
/// zurueckgenommenen Einladung, Monate spaeter.
/// </para>
/// <para>
/// Die REIHENFOLGE in der Aufzaehlung ist jetzt EFs (alphabetisch) statt der
/// des Python-Dienstes. Das ist harmlos, und zwar nachgesehen: keine Abfrage
/// sortiert nach <c>status</c> oder <c>action</c>. Wer je eine schreibt, muss
/// hierhin zurueck.
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
        string type, string[] labels, int members)
    {
        type.Should().NotBeEmpty();
        labels.Should().HaveCount(members);
        labels.Should().OnlyHaveUniqueItems();
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
        string type, string[] labels, int members)
    {
        members.Should().BePositive();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = """
            SELECT e.enumlabel
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            WHERE t.typname = @type
            ORDER BY e.enumsortorder
            """;
        befehl.Parameters.AddWithValue("type", type);

        var found = new List<string>();
        await using var leser = await befehl.ExecuteReaderAsync();
        while (await leser.ReadAsync())
        {
            found.Add(leser.GetString(0));
        }

        found.Should().BeEquivalentTo(labels);
    }

    /// <summary>
    /// Und gegen die SchemaMigration, die den Typ auf einer frischen Datenbank
    /// anlegt.
    /// </summary>
    /// <remarks>
    /// Eine SchemaMigration, die eine andere Menge anlegt als die hier festgenagelte,
    /// verschoebe den Fehlschlag nur auf die erste frische Datenbank.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Aufzaehlungen))]
    public void Die_Wanderung_legt_genau_diese_Etiketten_an(
        string type, string[] labels, int members)
    {
        members.Should().BePositive();

        var ordner = Path.Combine(
            Postgres.Repowurzel(),
            "src", "identity-service",
            "WorkerTransfer.Identity.Infrastructure", "Persistence", "Migrations");

        var anmerkung = Directory.EnumerateFiles(ordner, "*.cs")
            .Where(path => !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Select(text => Regex.Match(
                text, $@"""Npgsql:Enum:{type}"", ""(?<werte>[^""]*)""",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            .SingleOrDefault(hit => hit.Success);

        anmerkung.Should().NotBeNull($"genau eine Wanderung muss {type} anlegen");

        anmerkung!.Groups["werte"].Value.Split(',')
            .Should().BeEquivalentTo(labels);
    }
}
