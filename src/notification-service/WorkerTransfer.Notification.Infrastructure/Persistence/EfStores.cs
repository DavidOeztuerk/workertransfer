using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Domain.Notifications;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Reads and writes <c>notification_preferences</c>.</summary>
public sealed class EfPreferenceStore(NotificationDbContext context) : IPreferenceStore
{
    /// <inheritdoc />
    public async Task<NotificationPreference?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Preferences
            .FirstOrDefaultAsync(candidate => candidate.Id == subject.Value, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        NotificationPreference preference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        var existing = await context.Preferences
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == preference.Subject.Value, cancellationToken);

        if (existing is null)
        {
            context.Preferences.Add(new PreferenceRow
            {
                Id = preference.Subject.Value,
                Kinds = Write(preference.Switches),
                LastSentAt = preference.LastSentAt?.UtcDateTime
            });

            return;
        }

        existing.Kinds = Write(preference.Switches);
        existing.LastSentAt = preference.LastSentAt?.UtcDateTime;
    }

    private static string Write(IReadOnlyDictionary<NotificationKind, bool> switches) =>
        JsonSerializer.Serialize(
            switches.ToDictionary(pair => NotificationKinds.Word(pair.Key), pair => pair.Value));

    private static NotificationPreference ToAggregate(PreferenceRow row)
    {
        var read = JsonSerializer.Deserialize<Dictionary<string, bool>>(row.Kinds) ?? [];
        var switches = new Dictionary<NotificationKind, bool>();

        foreach (var (word, wanted) in read)
        {
            // A word that is not a kind is passed over instead of thrown on: a
            // deleted kind must not make an inbox unreadable.
            if (NotificationKinds.Read(word) is { } kind)
            {
                switches[kind] = wanted;
            }
        }

        return NotificationPreference.Restore(
            new SubjectId(row.Id),
            switches,
            row.LastSentAt is { } last ? new DateTimeOffset(last, TimeSpan.Zero) : null);
    }
}

/// <summary>Reads and writes <c>notifications</c>.</summary>
public sealed class EfInboxStore(NotificationDbContext context) : IInboxStore
{
    /// <inheritdoc />
    public Task AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        context.InboxEntries.Add(new InboxRow
        {
            Id = entry.Id,
            UserId = entry.Subject.Value,
            Kind = NotificationKinds.Word(entry.Kind),
            CreatedAt = entry.CreatedAt.UtcDateTime,
            ReadAt = entry.ReadAt?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxEntry>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.InboxEntries
            .Where(candidate => candidate.UserId == subject.Value)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public Task<int> ReadAllAsync(
        SubjectId subject, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.InboxEntries
            .Where(row => row.UserId == subject.Value && row.ReadAt == null)
            .ExecuteUpdateAsync(
                set => set.SetProperty(
                    row => row.ReadAt, (DateTime?)now.UtcDateTime),
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistedSinceAsync(
        SubjectId subject,
        NotificationKind kind,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var word = NotificationKinds.Word(kind);
        var from = since.UtcDateTime;

        // Exactly the columns the index lies on (user_id, created_at) — plus
        // the kind, which costs nothing more on this short set.
        //
        // STRICTLY GREATER, not "greater or equal": exactly one day later
        // something goes out again. The same boundary as the hourly throttle
        // (`now - last >= Throttle`) — two caps that decide differently at the
        // boundary are two rules that look like one.
        return context.InboxEntries.AnyAsync(
            row => row.UserId == subject.Value
                     && row.Kind == word
                     && row.CreatedAt > from,
            cancellationToken);
    }

    private static InboxEntry ToAggregate(InboxRow row) =>
        InboxEntry.Restore(
            row.Id,
            new SubjectId(row.UserId),
            NotificationKinds.Read(row.Kind)
            ?? throw new InvalidOperationException(
                $"Unbekannte Benachrichtigungsart in der Datenbank: {row.Kind}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.ReadAt is { } read ? new DateTimeOffset(read, TimeSpan.Zero) : null);
}
