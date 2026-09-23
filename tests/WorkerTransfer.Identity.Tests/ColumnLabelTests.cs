using System.Text.RegularExpressions;
using FluentAssertions;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// The exact label set behind every Postgres enumeration this service writes.
/// </summary>
/// <remarks>
/// For as long as Alembic owned the schema, a hand-written migration created
/// the type under a guard, and this test read its SQL. Since the move EF creates
/// the types itself — out of the annotation <c>Npgsql:Enum:&lt;type&gt;</c> in
/// the migration. What is checked is the same as before: <em>which</em> labels
/// come into being.
/// <para>
/// Every set is pinned three times over: spelled out, against the real column
/// and against the migration. A label this service knows and the column does not
/// is an insert that fails — with <c>invitation_withdrawn</c> perhaps only at
/// the first invitation ever taken back, months later.
/// </para>
/// <para>
/// The ORDER in the enumeration is EF's now (alphabetical) instead of the Python
/// service's. That is harmless, and it was looked up: no query sorts by
/// <c>status</c> or <c>action</c>. Whoever ever writes one has to come back
/// here.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ColumnLabelTests(Postgres postgres)
{
    private static readonly string[] AuditTrail =
    [
        "register", "login_success", "login_failure", "token_refresh", "token_revoke",
        "tenant_switch", "tenant_switch_denied", "email_verified", "company_created",
        "member_invited", "member_joined", "invitation_withdrawn", "member_removed"
    ];

    private static readonly string[] AccountStatusValue =
    [
        "pending", "active", "suspended", "disabled"
    ];

    public static TheoryData<string, string[], int> Enumerations => new()
    {
        { "audit_action", AuditTrail, 13 },
        { "account_status", AccountStatusValue, 4 }
    };

    [Theory]
    [MemberData(nameof(Enumerations))]
    public void There_is_exactly_one_label_per_member(
        string type, string[] labels, int members)
    {
        type.Should().NotBeEmpty();
        labels.Should().HaveCount(members);
        labels.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_enumerations_have_as_many_members_as_stated()
    {
        Enum.GetValues<AuditAction>().Should().HaveCount(AuditTrail.Length);
        Enum.GetValues<AccountStatus>().Should().HaveCount(AccountStatusValue.Length);
    }

    /// <summary>
    /// Against the type Alembic really created. A label this service knows and
    /// the column does not is an insert that fails; the other way round is a
    /// row Python can write and this service cannot read.
    /// </summary>
    [Theory]
    [MemberData(nameof(Enumerations))]
    public async Task The_column_knows_exactly_these_labels(
        string type, string[] labels, int members)
    {
        members.Should().BePositive();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.enumlabel
            FROM pg_enum e
            JOIN pg_type t ON t.oid = e.enumtypid
            WHERE t.typname = @type
            ORDER BY e.enumsortorder
            """;
        command.Parameters.AddWithValue("type", type);

        var found = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            found.Add(reader.GetString(0));
        }

        found.Should().BeEquivalentTo(labels);
    }

    /// <summary>
    /// And against the migration that creates the type on a fresh database.
    /// </summary>
    /// <remarks>
    /// A migration creating a different set from the one pinned here would only
    /// move the failure to the first fresh database.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Enumerations))]
    public void The_migration_creates_exactly_these_labels(
        string type, string[] labels, int members)
    {
        members.Should().BePositive();

        var folder = Path.Combine(
            Postgres.RepositoryRoot(),
            "src", "identity-service",
            "WorkerTransfer.Identity.Infrastructure", "Persistence", "Migrations");

        var migrationMatch = Directory.EnumerateFiles(folder, "*.cs")
            .Where(path => !path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal))
            .Select(File.ReadAllText)
            .Select(text => Regex.Match(
                text, $@"""Npgsql:Enum:{type}"", ""(?<values>[^""]*)""",
                RegexOptions.None, TimeSpan.FromSeconds(5)))
            .SingleOrDefault(hit => hit.Success);

        migrationMatch.Should().NotBeNull($"exactly one migration has to create {type}");

        migrationMatch!.Groups["values"].Value.Split(',')
            .Should().BeEquivalentTo(labels);
    }
}
