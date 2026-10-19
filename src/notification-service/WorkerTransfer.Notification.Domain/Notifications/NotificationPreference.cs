using Noelia.Core.Identity;

namespace WorkerTransfer.Notification.Domain.Notifications;

/// <summary>Eight switches and a throttle, all hanging on the person.</summary>
/// <remarks>
/// The throttle stands here and not in a table of its own: there is exactly one
/// per person, it is no object in its own right, and a second table would be a
/// join for one timestamp.
/// </remarks>
public sealed class NotificationPreference
{
    /// <summary>At most one message per person per hour, across all kinds.</summary>
    /// <remarks>
    /// Even a content-free mail gives away its timing: whoever watches when
    /// WorkerTransfer writes sees <em>when</em> something happens. The throttle
    /// takes the meaning out of the frequency — and incidentally makes the
    /// endpoint worthless as a tool for spamming.
    /// <para>
    /// One hour is a choice, not a derivation: shorter throttles nothing,
    /// because more time passes between two sessions anyway; longer swallows a
    /// real message.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan Throttle = TimeSpan.FromHours(1);

    private readonly Dictionary<NotificationKind, bool> _kinds;

    private NotificationPreference(
        SubjectId subject,
        Dictionary<NotificationKind, bool> kinds,
        DateTimeOffset? lastSentAt)
    {
        Subject = subject;
        _kinds = kinds;
        LastSentAt = lastSentAt;
    }

    /// <summary>Whose preferences. They <em>are</em> the key.</summary>
    public SubjectId Subject { get; }

    /// <summary>When something last went out, or <c>null</c>.</summary>
    public DateTimeOffset? LastSentAt { get; private set; }

    /// <summary>Everything on.</summary>
    /// <remarks>
    /// A notification about one's <em>own</em> case is not advertising but the
    /// condition for "the person decides" to be able to happen at all. Whoever
    /// has set nothing should learn that somebody wants something from them.
    /// </remarks>
    public static NotificationPreference Default(SubjectId subject) =>
        new(subject, NotificationKinds.All.ToDictionary(kind => kind, _ => true), null);

    /// <summary>The preferences as a row holds them.</summary>
    public static NotificationPreference Restore(
        SubjectId subject,
        IReadOnlyDictionary<NotificationKind, bool> kinds,
        DateTimeOffset? lastSentAt) =>
        new(subject, new Dictionary<NotificationKind, bool>(kinds), lastSentAt);

    /// <summary>Does the person want mail of this kind?</summary>
    /// <remarks>
    /// An unknown kind counts as wanted: a new kind must not stay away silently
    /// because an old row does not know it.
    /// </remarks>
    public bool Wants(NotificationKind kind) => _kinds.GetValueOrDefault(kind, true);

    /// <summary>Sets one switch.</summary>
    public void Set(NotificationKind kind, bool wanted) => _kinds[kind] = wanted;

    /// <summary>May something go out now?</summary>
    public bool MayNow(NotificationKind kind, DateTimeOffset now)
    {
        if (!Wants(kind))
        {
            return false;
        }

        return LastSentAt is not { } last || now - last >= Throttle;
    }

    /// <summary>Remembers that something went out.</summary>
    public void MarkSent(DateTimeOffset now) => LastSentAt = now;

    /// <summary>The switches, for writing into a row.</summary>
    public IReadOnlyDictionary<NotificationKind, bool> Switches => _kinds;
}

/// <summary>One entry in the person's inbox.</summary>
/// <remarks>
/// <strong>No content.</strong> The same rule as for the outbox, and for the
/// same reason: what stands here ends up in every backup. A column for the text
/// would be an invitation to write "Acme GmbH would like to see your market
/// status" into it. The kind is enough to show the right place in the
/// application — and the application checks who reads.
/// </remarks>
public sealed class InboxEntry
{
    private InboxEntry(
        Guid id,
        SubjectId subject,
        NotificationKind kind,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt)
    {
        Id = id;
        Subject = subject;
        Kind = kind;
        CreatedAt = createdAt;
        ReadAt = readAt;
    }

    /// <summary>Which entry.</summary>
    public Guid Id { get; }

    /// <summary>For whom.</summary>
    public SubjectId Subject { get; }

    /// <summary>About what.</summary>
    public NotificationKind Kind { get; }

    /// <summary>When it came into being.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary><c>null</c> for as long as it is unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Creates an entry.</summary>
    public static InboxEntry Create(SubjectId subject, NotificationKind kind, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, kind, now, null);

    /// <summary>The entry as a row holds it.</summary>
    public static InboxEntry Restore(
        Guid id,
        SubjectId subject,
        NotificationKind kind,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt) =>
        new(id, subject, kind, createdAt, readAt);

    /// <summary>Marks it read. A second time changes nothing.</summary>
    public void Read(DateTimeOffset now) => ReadAt ??= now;
}

/// <summary>Finds and stores preferences.</summary>
public interface IPreferenceStore
{
    /// <summary>This person's preferences, or <c>null</c>.</summary>
    Task<NotificationPreference?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(
        NotificationPreference preference, CancellationToken cancellationToken = default);
}

/// <summary>Finds and stores inbox entries.</summary>
public interface IInboxStore
{
    /// <summary>Creates an entry.</summary>
    Task AppendAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>This person's inbox, newest first.</summary>
    Task<IReadOnlyList<InboxEntry>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Marks everything unread as read.</summary>
    /// <returns>How many entries that touched.</returns>
    Task<int> ReadAllAsync(
        SubjectId subject, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Has there been an entry of this kind since <paramref name="since"/>?</summary>
    /// <remarks>
    /// <para>The daily cap from ADR-0033, and it is answered out of the
    /// <em>inbox</em> instead of out of a column of its own. That is the point:
    /// a column "last discovered at" would be a second line about a person's
    /// visibility, and the inbox already holds the same information — it
    /// belongs to them.</para>
    ///
    /// <para>It incidentally carries the at-least-once delivery too: a second
    /// outbox row of the same kind on the same day creates nothing new.</para>
    /// </remarks>
    Task<bool> ExistedSinceAsync(
        SubjectId subject,
        NotificationKind kind,
        DateTimeOffset since,
        CancellationToken cancellationToken = default);
}
