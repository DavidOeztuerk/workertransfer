using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>The log, on Postgres.</summary>
/// <remarks>
/// Note what is absent here too: no update, no delete. The port offers neither,
/// and neither does this.
/// </remarks>
public sealed class EfConsentLedger(ConsentDbContext context) : IConsentLedger
{
    /// <summary>
    /// The reduction, once, as SQL — and the only definition of "newest".
    /// </summary>
    /// <remarks>
    /// <c>DISTINCT ON</c> is the Postgres-native way to take one row per group
    /// without a window function or a self-join, and it matches
    /// <c>ix_consent_events_lookup</c> exactly: the <c>ORDER BY</c> leads with
    /// the <c>DISTINCT ON</c> columns and then takes the newest fact, tying on
    /// <c>event_id</c>.
    /// <para>
    /// Three reads share it and differ only in the filter. Two reductions that
    /// can drift apart would be worse than one read fewer:
    /// <c>/consent/me</c> and <c>/consent/check</c> would then disagree about
    /// the same pair, and nobody would be able to say which one lied.
    /// </para>
    /// <para>
    /// Written out rather than expressed in LINQ because the tie-break cannot
    /// be: .NET has no <c>&gt;</c> for an id, and <c>Guid.CompareTo</c> orders
    /// by field where Postgres orders by byte.
    /// </para>
    /// </remarks>
    private const string Latest = """
        SELECT DISTINCT ON (subject_id, capability)
               id, event_id, subject_id, capability, action, actor_id, reason, metadata,
               recorded_at
        FROM consent_events
        WHERE
        """;

    private const string Ordering = """
        ORDER BY subject_id, capability, recorded_at DESC, event_id DESC
        """;

    /// <inheritdoc />
    public async Task AttachAsync(
        ConsentEvent consentEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consentEvent);

        context.ConsentEvents.Add(new ConsentEventRow
        {
            EventId = consentEvent.EventId,
            SubjectId = consentEvent.Subject.Value,
            Capability = consentEvent.Capability.Value,
            Action = consentEvent.Action,
            ActorId = consentEvent.Actor?.Value,
            Reason = consentEvent.Reason?.Value,
            Metadata = JsonSerializer.Serialize(consentEvent.Metadata),
            RecordedAt = consentEvent.RecordedAt.UtcDateTime
        });

        // Written through inside the transaction the caller holds, not at commit
        // time. Two things depend on it: a duplicate event id fails here, where
        // the caller can still see which write caused it, and the read that
        // follows in the same handler sees the fact that was just appended.
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEvent>> HistoryAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.ConsentEvents
            .Where(row => row.SubjectId == subject.Value)
            .OrderBy(row => row.RecordedAt)
            .ThenBy(row => row.EventId)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToFact)];
    }

    /// <inheritdoc />
    public async Task<ConsentEvent?> LatestOneAsync(
        SubjectId subject, Capability capability, CancellationToken cancellationToken = default)
    {
        var rows = await context.ConsentEvents
            .FromSqlRaw(
                $"{Latest} subject_id = {{0}} AND capability = {{1}} {Ordering}",
                subject.Value, capability.Value)
            .ToListAsync(cancellationToken);

        return rows.Count is 0 ? null : ToFact(rows[0]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(Guid Subject, string Capability), ConsentEvent>>
        LatestAsync(
            IReadOnlyList<(SubjectId Subject, Capability Capability)> pairs,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pairs);

        if (pairs.Count is 0)
        {
            return new Dictionary<(Guid, string), ConsentEvent>();
        }

        // The pairs go over as two arrays and are put back together by the
        // database. The alternative — two IN lists — would ask about every
        // combination of the subjects and the capabilities named, and answer
        // about pairs nobody asked about. A batch is meant to make one question
        // cheaper, not a hundred questions into ten thousand.
        var subjects = pairs.Select(pair => pair.Subject.Value).ToArray();
        var skills = pairs.Select(pair => pair.Capability.Value).ToArray();

        var rows = await context.ConsentEvents
            .FromSqlRaw(
                $"{Latest} (subject_id, capability) IN "
                + "(SELECT s, c FROM unnest({0}::uuid[], {1}::text[]) AS t(s, c)) "
                + Ordering,
                subjects, skills)
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            row => (row.SubjectId, row.Capability),
            ToFact);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEvent>> LatestPerSkillAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.ConsentEvents
            .FromSqlRaw($"{Latest} subject_id = {{0}} {Ordering}", subject.Value)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToFact)];
    }

    private static ConsentEvent ToFact(ConsentEventRow row) => ConsentEvent.Restore(
        row.EventId,
        new SubjectId(row.SubjectId),
        Capability.Parse(row.Capability),
        row.Action,
        new DateTimeOffset(DateTime.SpecifyKind(row.RecordedAt, DateTimeKind.Utc)),
        row.ActorId is { } actorId ? new SubjectId(actorId) : null,
        row.Reason is { } reason ? WithdrawalReason.Parse(reason) : null,
        JsonSerializer.Deserialize<Dictionary<string, string>>(row.Metadata));
}
