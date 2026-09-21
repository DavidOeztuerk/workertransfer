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
public class AuditTrailTests(Postgres postgres)
{
    private IdentityDbContext Context() =>
        IdentityDbContextFactory.For(postgres.ConnectionString);

    /// <summary>
    /// All thirteen, one by one. A member whose label the column does not know
    /// fails on the first write of that action — which, for
    /// <c>invitation_withdrawn</c>, could be months after the mistake.
    /// </summary>
    [Fact]
    public async Task Every_action_can_be_written_and_read_back()
    {
        var alle = Enum.GetValues<AuditAction>();
        alle.Should().HaveCount(13);

        foreach (var action in alle)
        {
            var subject = SubjectId.New();

            await using (var context = Context())
            {
                await new EfAuditTrail(context).AppendAsync(
                    new AuditEvent(action, DateTimeOffset.UtcNow, actor: subject));
                await context.SaveChangesAsync();
            }

            await using var read = Context();
            var row = await read.AuditEvents
                .FirstOrDefaultAsync(row => row.ActorId == subject.Value);

            row.Should().NotBeNull($"{action} has to fit into audit_events");
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
    public async Task The_action_stands_there_under_the_name_Python_wrote(
        AuditAction action, string expected)
    {
        var subject = SubjectId.New();

        await using (var context = Context())
        {
            await new EfAuditTrail(context).AppendAsync(
                new AuditEvent(action, DateTimeOffset.UtcNow, actor: subject));
            await context.SaveChangesAsync();
        }

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT action::text FROM audit_events WHERE actor_id = @wer";
        command.Parameters.AddWithValue("wer", subject.Value);

        (await command.ExecuteScalarAsync()).Should().Be(expected);
    }

    [Fact]
    public async Task The_metadata_lands_as_jsonb_and_comes_back_that_way()
    {
        var subject = SubjectId.New();

        await using (var context = Context())
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
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT metadata->>'reason', correlation_id FROM audit_events WHERE actor_id = @wer";
        command.Parameters.AddWithValue("wer", subject.Value);

        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        reader.GetString(0).Should().Be("bad_password");
        reader.GetString(1).Should().Be("abc-123");
    }

    /// <summary>
    /// Nothing about a person reaches the trail. It is read long afterwards by
    /// people who were not there, and whatever got in once is in every backup.
    /// </summary>
    [Theory]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("display_name")]
    public void A_key_outside_the_allowlist_does_not_get_in_at_all(
        string key)
    {
        var build = () => new AuditEvent(
            AuditAction.Register,
            DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = "egal"
            });

        build.Should().Throw<AuditMetadataException>().Which.Key.Should().Be(key);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("ip")]
    [InlineData("user_agent")]
    [InlineData("role")]
    public void The_four_allowed_keys_go_through(string key)
    {
        var build = () => new AuditEvent(
            AuditAction.Register,
            DateTimeOffset.UtcNow,
            metadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = "egal"
            });

        build.Should().NotThrow();
    }
}
