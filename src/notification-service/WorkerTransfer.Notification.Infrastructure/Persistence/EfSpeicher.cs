using System.Text.Json;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Infrastructure.Persistence;

/// <summary>Liest und schreibt <c>notification_preferences</c>.</summary>
public sealed class EfPreferenceStore(NotificationDbContext context) : IPreferenceStore
{
    /// <inheritdoc />
    public async Task<NotificationPreference?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Wuensche
            .FirstOrDefaultAsync(kandidat => kandidat.Id == subject.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        NotificationPreference preference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        var vorhanden = await context.Wuensche
            .AsTracking()
            .FirstOrDefaultAsync(kandidat => kandidat.Id == preference.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            context.Wuensche.Add(new PreferenceRow
            {
                Id = preference.Wer.Value,
                Kinds = Write(preference.Schalter),
                LastSentAt = preference.ZuletztGesendetAm?.UtcDateTime
            });

            return;
        }

        vorhanden.Kinds = Write(preference.Schalter);
        vorhanden.LastSentAt = preference.ZuletztGesendetAm?.UtcDateTime;
    }

    private static string Write(IReadOnlyDictionary<Benachrichtigungsart, bool> schalter) =>
        JsonSerializer.Serialize(
            schalter.ToDictionary(paar => Benachrichtigungsarten.Wort(paar.Key), paar => paar.Value));

    private static NotificationPreference ZumAggregat(PreferenceRow row)
    {
        var gelesen = JsonSerializer.Deserialize<Dictionary<string, bool>>(row.Kinds) ?? [];
        var schalter = new Dictionary<Benachrichtigungsart, bool>();

        foreach (var (word, gewollt) in gelesen)
        {
            // Ein Wort, das keine Art ist, wird übergangen statt geworfen: eine
            // gestrichene Art soll kein Inbox unlesbar machen.
            if (Benachrichtigungsarten.Lies(word) is { } kind)
            {
                schalter[kind] = gewollt;
            }
        }

        return NotificationPreference.Stelle_her(
            new SubjectId(row.Id),
            schalter,
            row.LastSentAt is { } zuletzt ? new DateTimeOffset(zuletzt, TimeSpan.Zero) : null);
    }
}

/// <summary>Liest und schreibt <c>notifications</c>.</summary>
public sealed class EfInboxStore(NotificationDbContext context) : IInboxStore
{
    /// <inheritdoc />
    public Task AppendAsync(Eingang eingang, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eingang);

        context.Eingaenge.Add(new InboxRow
        {
            Id = eingang.Id,
            UserId = eingang.Wer.Value,
            Kind = Benachrichtigungsarten.Wort(eingang.Art),
            CreatedAt = eingang.AngelegtAm.UtcDateTime,
            ReadAt = eingang.GelesenAm?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Eingang>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Eingaenge
            .Where(kandidat => kandidat.UserId == subject.Value)
            .OrderByDescending(kandidat => kandidat.CreatedAt)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ZumAggregat)];
    }

    /// <inheritdoc />
    public Task<int> ReadAllAsync(
        SubjectId subject, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.Eingaenge
            .Where(row => row.UserId == subject.Value && row.ReadAt == null)
            .ExecuteUpdateAsync(
                setzen => setzen.SetProperty(
                    row => row.ReadAt, (DateTime?)now.UtcDateTime),
                cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistedSinceAsync(
        SubjectId subject,
        Benachrichtigungsart kind,
        DateTimeOffset seit,
        CancellationToken cancellationToken = default)
    {
        var word = Benachrichtigungsarten.Wort(kind);
        var ab = seit.UtcDateTime;

        // Genau die Spalten, auf denen der Index liegt (user_id, created_at) —
        // plus die Art, die auf dieser kurzen Menge nichts mehr kostet.
        //
        // ECHT GROESSER, nicht „groesser gleich": genau einen Tag spaeter geht
        // wieder etwas hinaus. Dieselbe Grenze wie bei der stuendlichen Drossel
        // (`jetzt - zuletzt >= Drossel`) — zwei Kappen, die an der Grenze
        // verschieden entscheiden, sind zwei Regeln, die aussehen wie eine.
        return context.Eingaenge.AnyAsync(
            row => row.UserId == subject.Value
                     && row.Kind == word
                     && row.CreatedAt > ab,
            cancellationToken);
    }

    private static Eingang ZumAggregat(InboxRow row) =>
        Eingang.Stelle_her(
            row.Id,
            new SubjectId(row.UserId),
            Benachrichtigungsarten.Lies(row.Kind)
            ?? throw new InvalidOperationException(
                $"Unbekannte Benachrichtigungsart in der Datenbank: {row.Kind}"),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            row.ReadAt is { } gelesen ? new DateTimeOffset(gelesen, TimeSpan.Zero) : null);
}
