using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace WorkerTransfer.Outbox.Tests;

/// <summary>A delivery a test can steer.</summary>
public sealed class ProbeDelivery : IDelivery
{
    private readonly Func<SubjectId, string, Task> _behavior;

    public ProbeDelivery(Func<SubjectId, string, Task>? behavior = null) =>
        _behavior = behavior ?? ((_, _) => Task.CompletedTask);

    public List<(SubjectId Recipient, string Kind)> Delivered { get; } = [];

    public async Task DeliverAsync(
        SubjectId recipient, string kind, CancellationToken cancellationToken = default)
    {
        await _behavior(recipient, kind);
        Delivered.Add((recipient, kind));
    }
}

/// <summary>The outbox, against a real database.</summary>
[Collection(PostgresCollection.Name)]
public class OutboxTests(Postgres postgres)
{
    private ContextFactory Factory() => new(postgres.ConnectionString);

    /// <summary>
    /// A dispatcher with a context of its own — the way the loop builds one per
    /// pass, from a scope of its own.
    /// </summary>
    private OutboxDispatcher<ProbeContext> Dispatcher(
        ProbeContext context,
        IDelivery delivery,
        OutboxSettings? settings = null) =>
        new(context, delivery, TimeProvider.System,
            NullLogger<OutboxDispatcher<ProbeContext>>.Instance, settings);

    private async Task<(int Due, int Delivered)> Pass(
        IDelivery delivery, OutboxSettings? settings = null)
    {
        await using var context = Factory().CreateDbContext();
        return await Dispatcher(context, delivery, settings).PassAsync();
    }

    private async Task<SubjectId> RecordIntent(string kind, int howMany = 1)
    {
        var subject = SubjectId.New();

        await using var context = Factory().CreateDbContext();
        var outbox = new EfOutbox<ProbeContext>(context, TimeProvider.System);

        for (var i = 0; i < howMany; i++)
        {
            await outbox.NoteAsync(subject, kind);
        }

        await context.SaveChangesAsync();
        return subject;
    }

    private async Task<OutboxRow> Row(SubjectId subject)
    {
        await using var context = Factory().CreateDbContext();
        return await context.Set<OutboxRow>().SingleAsync(row => row.UserId == subject.Value);
    }

    /// <summary>
    /// Written independently, the notification could exist while the change was
    /// rolled back — somebody would be told their transfer was accepted and the
    /// database would hold nothing of the sort.
    /// </summary>
    [Fact]
    public async Task A_rollback_takes_the_intent_with_it()
    {
        var subject = SubjectId.New();

        await using (var context = Factory().CreateDbContext())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await new EfOutbox<ProbeContext>(context, TimeProvider.System)
                .NoteAsync(subject, "transfer.accepted");
            await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var readBack = Factory().CreateDbContext();
        (await readBack.Set<OutboxRow>().CountAsync(row => row.UserId == subject.Value))
            .Should().Be(0);
    }

    [Fact]
    public async Task A_recorded_intent_is_delivered_and_ticked_off()
    {
        var subject = await RecordIntent("transfer.accepted");
        var delivery = new ProbeDelivery();

        var (due, delivered) = await Pass(delivery);

        due.Should().BeGreaterThan(0);
        delivered.Should().BeGreaterThan(0);
        delivery.Delivered.Should().Contain((subject, "transfer.accepted"));
        (await Row(subject)).DeliveredAt.Should().NotBeNull();
    }

    /// <summary>
    /// Only the kind of failure, never the other side's answer — a recipient
    /// that echoes a request body would otherwise write it into a table that
    /// ends up in every backup.
    /// </summary>
    [Fact]
    public async Task A_failure_counts_up_and_stores_only_the_kind_of_failure()
    {
        var subject = await RecordIntent("transfer.accepted");
        var delivery = new ProbeDelivery((_, _) =>
            throw new HttpRequestException("Anna Example, born 1984, living at ..."));

        await Pass(delivery);

        var row = await Row(subject);
        row.Attempts.Should().Be(1);
        row.DeliveredAt.Should().BeNull();
        row.LastError.Should().Be("HttpRequestException");
        row.LastError.Should().NotContain("Anna");
    }

    /// <summary>
    /// Not a failure, so no attempt and no trace. Offering the row again on the
    /// next tick IS how the order of ADR-0027 §6 is kept.
    /// </summary>
    [Fact]
    public async Task Not_yet_spends_no_attempt()
    {
        var subject = await RecordIntent("erasure:final");
        var delivery = new ProbeDelivery(
            (_, _) => throw new NotYetException("acknowledge first"));

        var (due, delivered) = await Pass(delivery);

        due.Should().BeGreaterThan(0);
        delivered.Should().Be(0);

        var row = await Row(subject);
        row.Attempts.Should().Be(0, "waiting one's turn is not a failure");
        row.LastError.Should().BeEmpty();
        row.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// Giving up is not deleting. A row that vanishes quietly is exactly the
    /// state this table abolishes.
    /// </summary>
    [Fact]
    public async Task After_the_ceiling_the_row_stays_lying_instead_of_vanishing()
    {
        var subject = await RecordIntent("transfer.accepted");
        var delivery = new ProbeDelivery((_, _) => throw new HttpRequestException("gone"));
        var settings = new OutboxSettings { HoechsteVersuche = 2 };

        for (var i = 0; i < 5; i++)
        {
            await Pass(delivery, settings);
        }

        var row = await Row(subject);
        row.Attempts.Should().Be(2, "after that it is no longer due");
        row.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// For an erasure, "give up after ten" would be the silent failure ADR-0027
    /// exists against: a promise nobody redeems, and nobody sees it.
    /// </summary>
    [Fact]
    public async Task Without_a_ceiling_the_row_stays_due()
    {
        var subject = await RecordIntent("erasure:consent");
        var delivery = new ProbeDelivery((_, _) => throw new HttpRequestException("dead"));
        var settings = new OutboxSettings { HoechsteVersuche = null };

        for (var i = 0; i < 12; i++)
        {
            await Pass(delivery, settings);
        }

        var row = await Row(subject);
        row.Attempts.Should().Be(12, "whoever never gives up stays due");
        row.DeliveredAt.Should().BeNull();
    }

    /// <summary>
    /// Two dispatchers, one table. Without the lock both grab the same rows and
    /// every intent goes out twice — which is why the Python chart is pinned to
    /// a single replica.
    /// </summary>
    /// <remarks>
    /// The competitor is a plain transaction holding the same lock, not a second
    /// dispatcher waiting on a signal: a test that coordinates two background
    /// passes can hang instead of failing, and a hanging test says nothing.
    /// </remarks>
    [Fact]
    public async Task Locked_rows_are_skipped_instead_of_delivered_twice()
    {
        await RecordIntent("transfer.accepted", howMany: 5);

        await using var competitor = Factory().CreateDbContext();
        await using var transaction = await competitor.Database.BeginTransactionAsync();

        var locked = await competitor.Set<OutboxRow>()
            .FromSqlRaw(
                "SELECT * FROM \"outbox\" WHERE delivered_at IS NULL "
                + "ORDER BY created_at FOR UPDATE SKIP LOCKED")
            .ToListAsync();

        locked.Should().NotBeEmpty("otherwise the test checks nothing");

        var delivery = new ProbeDelivery();
        var (due, delivered) = await Pass(delivery);

        due.Should().Be(0, "the locked rows belong to the other pass");
        delivered.Should().Be(0);
        delivery.Delivered.Should().BeEmpty();

        await transaction.RollbackAsync();
    }
}
