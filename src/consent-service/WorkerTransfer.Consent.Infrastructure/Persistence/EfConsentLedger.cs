using System.Text.Json;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Infrastructure.Persistence;

/// <summary>The log, on Postgres.</summary>
/// <remarks>
/// Note what is absent here too: no update, no delete. The port offers neither,
/// and neither does this.
/// </remarks>
public sealed class EfConsentLedger(ConsentDbContext kontext) : IConsentLedger
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
    private const string Neueste = """
        SELECT DISTINCT ON (subject_id, capability)
               id, event_id, subject_id, capability, action, actor_id, reason, metadata,
               recorded_at
        FROM consent_events
        WHERE
        """;

    private const string Ordnung = """
        ORDER BY subject_id, capability, recorded_at DESC, event_id DESC
        """;

    /// <inheritdoc />
    public async Task AnhaengenAsync(
        ConsentEvent ereignis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ereignis);

        kontext.ConsentEvents.Add(new ConsentEventRow
        {
            EventId = ereignis.EventId,
            SubjectId = ereignis.Subject.Value,
            Capability = ereignis.Capability.Value,
            Action = ereignis.Action,
            ActorId = ereignis.Actor?.Value,
            Reason = ereignis.Reason?.Value,
            Metadata = JsonSerializer.Serialize(ereignis.Metadata),
            RecordedAt = ereignis.RecordedAt.UtcDateTime
        });

        // Written through inside the transaction the caller holds, not at commit
        // time. Two things depend on it: a duplicate event id fails here, where
        // the caller can still see which write caused it, and the read that
        // follows in the same handler sees the fact that was just appended.
        await kontext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEvent>> VerlaufAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.ConsentEvents
            .Where(zeile => zeile.SubjectId == subject.Value)
            .OrderBy(zeile => zeile.RecordedAt)
            .ThenBy(zeile => zeile.EventId)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZuFakt)];
    }

    /// <inheritdoc />
    public async Task<ConsentEvent?> NeuestesAsync(
        SubjectId subject, Capability capability, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.ConsentEvents
            .FromSqlRaw(
                $"{Neueste} subject_id = {{0}} AND capability = {{1}} {Ordnung}",
                subject.Value, capability.Value)
            .ToListAsync(cancellationToken);

        return zeilen.Count is 0 ? null : ZuFakt(zeilen[0]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<(Guid Subject, string Capability), ConsentEvent>>
        NeuesteAsync(
            IReadOnlyList<(SubjectId Subject, Capability Capability)> paare,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paare);

        if (paare.Count is 0)
        {
            return new Dictionary<(Guid, string), ConsentEvent>();
        }

        // The pairs go over as two arrays and are put back together by the
        // database. The alternative — two IN lists — would ask about every
        // combination of the subjects and the capabilities named, and answer
        // about pairs nobody asked about. A batch is meant to make one question
        // cheaper, not a hundred questions into ten thousand.
        var subjekte = paare.Select(paar => paar.Subject.Value).ToArray();
        var faehigkeiten = paare.Select(paar => paar.Capability.Value).ToArray();

        var zeilen = await kontext.ConsentEvents
            .FromSqlRaw(
                $"{Neueste} (subject_id, capability) IN "
                + "(SELECT s, c FROM unnest({0}::uuid[], {1}::text[]) AS t(s, c)) "
                + Ordnung,
                subjekte, faehigkeiten)
            .ToListAsync(cancellationToken);

        return zeilen.ToDictionary(
            zeile => (zeile.SubjectId, zeile.Capability),
            ZuFakt);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConsentEvent>> NeuesteJeFaehigkeitAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var zeilen = await kontext.ConsentEvents
            .FromSqlRaw($"{Neueste} subject_id = {{0}} {Ordnung}", subject.Value)
            .ToListAsync(cancellationToken);

        return [.. zeilen.Select(ZuFakt)];
    }

    private static ConsentEvent ZuFakt(ConsentEventRow zeile) => ConsentEvent.Restore(
        zeile.EventId,
        new SubjectId(zeile.SubjectId),
        Capability.Parse(zeile.Capability),
        zeile.Action,
        new DateTimeOffset(DateTime.SpecifyKind(zeile.RecordedAt, DateTimeKind.Utc)),
        zeile.ActorId is { } akteur ? new SubjectId(akteur) : null,
        zeile.Reason is { } grund ? WithdrawalReason.Parse(grund) : null,
        JsonSerializer.Deserialize<Dictionary<string, string>>(zeile.Metadata));
}
