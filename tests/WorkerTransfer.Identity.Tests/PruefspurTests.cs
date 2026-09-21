using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>The trail, against the real <c>audit_events</c> table.</summary>
/// <remarks>
/// Against the real table because everything that can be wrong here is
/// invisible otherwise: the column is a Postgres enum, the metadata is jsonb,
/// and the labels are produced by Npgsql's casing rule rather than by anything
/// written down in this repository.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class PruefspurTests(Postgres postgres)
{
    private IdentityDbContext Kontext() =>
        IdentityDbContextFactory.Fuer(postgres.ConnectionString);

    /// <summary>
    /// All thirteen, one by one. A member whose label the column does not know
    /// fails on the first write of that action — which, for
    /// <c>invitation_withdrawn</c>, could be months after the mistake.
    /// </summary>
    [Fact]
    public async Task Jede_Handlung_laesst_sich_schreiben_und_wiederlesen()
    {
        var alle = Enum.GetValues<AuditAction>();
        alle.Should().HaveCount(13);

        foreach (var action in alle)
        {
            var subject = SubjectId.New();

            await using (var context = Kontext())
            {
                await new EfAuditTrail(context).AppendAsync(
                    new AuditEvent(action, DateTimeOffset.UtcNow, actor: subject));
                await context.SaveChangesAsync();
            }

            await using var gelesen = Kontext();
            var row = await gelesen.AuditEvents
                .FirstOrDefaultAsync(row => row.ActorId == subject.Value);

            row.Should().NotBeNull($"{action} muss in audit_events passen");
            row!.Action.Should().Be(action);
        }
    }

    /// <summary>
    /// The label in the database, not the one .NET would print. This is the
    /// contract with the rows Python already wrote.
    /// </summary>
    [Theory]
    [InlineData(AuditAction.LoginSuccess, "login_success")]
    [InlineData(AuditAction.LoginFailure, "login_failure")]
    [InlineData(AuditAction.TokenRefresh, "token_refresh")]
    [InlineData(AuditAction.TenantSwitchDenied, "tenant_switch_denied")]
    [InlineData(AuditAction.InvitationWithdrawn, "invitation_withdrawn")]
    public async Task Die_Handlung_steht_als_der_Name_da_den_Python_geschrieben_hat(
        AuditAction action, string erwartet)
    {
        var subject = SubjectId.New();

        await using (var context = Kontext())
        {
            await new EfAuditTrail(context).AppendAsync(
                new AuditEvent(action, DateTimeOffset.UtcNow, actor: subject));
            await context.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText = "SELECT action::text FROM audit_events WHERE actor_id = @wer";
        befehl.Parameters.AddWithValue("wer", subject.Value);

        (await befehl.ExecuteScalarAsync()).Should().Be(erwartet);
    }

    [Fact]
    public async Task Die_Metadaten_landen_als_jsonb_und_kommen_so_zurueck()
    {
        var subject = SubjectId.New();

        await using (var context = Kontext())
        {
            await new EfAuditTrail(context).AppendAsync(
                new AuditEvent(
                    AuditAction.LoginFailure,
                    DateTimeOffset.UtcNow,
                    actor: subject,
                    correlationId: "abc-123",
                    metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["reason"] = "bad_password"
                    }));
            await context.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var befehl = connection.CreateCommand();
        befehl.CommandText =
            "SELECT metadata->>'reason', correlation_id FROM audit_events WHERE actor_id = @wer";
        befehl.Parameters.AddWithValue("wer", subject.Value);

        await using var leser = await befehl.ExecuteReaderAsync();
        (await leser.ReadAsync()).Should().BeTrue();
        leser.GetString(0).Should().Be("bad_password");
        leser.GetString(1).Should().Be("abc-123");
    }

    /// <summary>
    /// Nothing about a person reaches the trail. It is read long afterwards by
    /// people who were not there, and whatever got in once is in every backup.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("display_name")]
    public void Ein_Schluessel_ausserhalb_der_Erlaubnisliste_kommt_gar_nicht_erst_hinein(
        string key)
    {
        var bauen = () => new AuditEvent(
            AuditAction.Register,
            DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = "egal"
            });

        bauen.Should().Throw<AuditMetadataException>().Which.Key.Should().Be(key);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("ip")]
    [InlineData("user_agent")]
    [InlineData("role")]
    public void Die_vier_erlaubten_Schluessel_gehen_durch(string key)
    {
        var bauen = () => new AuditEvent(
            AuditAction.Register,
            DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = "egal"
            });

        bauen.Should().NotThrow();
    }
}
