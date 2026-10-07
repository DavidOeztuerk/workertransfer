using Noelia.Core.Identity;

namespace WorkerTransfer.Messages.Domain.Conversations;

/// <summary>Which side of a conversation a message belongs to.</summary>
/// <remarks>
/// Two and no third. The advisor (Phase 3) writes on the person's side, the
/// recruiter agent (Phase 2) on the company's — an agent for both sides does
/// not exist (VISION-FRAGEN D4).
/// </remarks>
public enum Side
{
    /// <summary>The natural person.</summary>
    Person,

    /// <summary>The company, through one of its members.</summary>
    Company
}

/// <summary>Who put the words together.</summary>
/// <remarks>
/// Released is always by a human being; this says only whether a model drafted
/// it. Shown to the own side, never to the other (ADR-0047, J2).
/// </remarks>
public enum Origin
{
    /// <summary>A human being wrote it.</summary>
    Human,

    /// <summary>A model drafted it, a human being released it.</summary>
    Agent
}

/// <summary>The kinds of process a conversation may hang on.</summary>
/// <remarks>
/// One in Phase 1. A new kind comes with its own answer to "who belongs to it",
/// and that answer lives in the service that owns the process — never here.
/// </remarks>
public static class ProcessKinds
{
    /// <summary>An application to a job advert.</summary>
    public const string Application = "application";

    /// <summary>
    /// A company's first contact to a person who released their profile to it
    /// (ADR-0048, VISION-FRAGEN B2). Who belongs to it stands in the
    /// conversation itself; the ledger decides, at every request, whether the
    /// company may still write.
    /// </summary>
    public const string Approach = "approach";

    /// <summary>Whether this service knows the kind.</summary>
    public static bool IsKnown(string kind) => kind is Application or Approach;

    /// <summary>The reference of the one approach between this company and this person.</summary>
    /// <remarks>
    /// Derived, not random: a second approach by the same company lands in the
    /// SAME conversation instead of opening a parallel one — two threads with
    /// the same counterpart would be a way to write past an unanswered first
    /// message. And it is not the person's id: the reference stands in URLs,
    /// and a subject id there would be a handle on the person.
    /// </remarks>
    public static Guid ApproachReference(TenantId company, SubjectId person)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"approach:{company.Value:N}:{person.Value:N}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

/// <summary>Something about the input is wrong.</summary>
public abstract class InputError(string message) : Exception(message);

/// <summary>Nothing to send.</summary>
public sealed class EmptyMessage() : InputError("text is required");

/// <summary>Too long for a message.</summary>
public sealed class MessageTooLong(int limit)
    : InputError($"The message is longer than {limit} characters.");

/// <summary>Only a draft can be released or discarded.</summary>
public sealed class NotADraft() : InputError("This message is not a draft.");

/// <summary>One conversation per process.</summary>
/// <remarks>
/// It names its two parties so a list can be read without asking anybody — but
/// whether somebody may <em>open</em> it is asked of the process's own service
/// every time (ADR-0047).
/// </remarks>
public sealed class Conversation
{
    private Conversation(
        Guid id, string kind, Guid reference, SubjectId person, TenantId company,
        DateTimeOffset createdAt, DateTimeOffset lastMessageAt)
    {
        Id = id;
        Kind = kind;
        Reference = reference;
        Person = person;
        Company = company;
        CreatedAt = createdAt;
        LastMessageAt = lastMessageAt;
    }

    /// <summary>Which conversation.</summary>
    public Guid Id { get; }

    /// <summary>The kind of process, e.g. <c>application</c>.</summary>
    public string Kind { get; }

    /// <summary>The process's id in its own service.</summary>
    public Guid Reference { get; }

    /// <summary>The natural person.</summary>
    public SubjectId Person { get; }

    /// <summary>The company.</summary>
    public TenantId Company { get; }

    /// <summary>When the first message was written.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the last message was sent — drafts do not move it.</summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>A new conversation, at its first message.</summary>
    public static Conversation Open(
        string kind, Guid reference, SubjectId person, TenantId company, DateTimeOffset now)
    {
        if (!ProcessKinds.IsKnown(kind))
        {
            throw new ArgumentException($"Unknown process kind '{kind}'.", nameof(kind));
        }

        return new Conversation(Guid.CreateVersion7(), kind, reference, person, company, now, now);
    }

    /// <summary>Back from the store.</summary>
    public static Conversation Restore(
        Guid id, string kind, Guid reference, SubjectId person, TenantId company,
        DateTimeOffset createdAt, DateTimeOffset lastMessageAt) =>
        new(id, kind, reference, person, company, createdAt, lastMessageAt);

    /// <summary>A message was sent.</summary>
    public void Touch(DateTimeOffset now)
    {
        if (now > LastMessageAt)
        {
            LastMessageAt = now;
        }
    }

    /// <summary>Who is on the other side of <paramref name="side"/>.</summary>
    public static Side Other(Side side) => side == Side.Person ? Side.Company : Side.Person;
}

/// <summary>A message — sent, or a draft only its own side sees.</summary>
public sealed class Message
{
    /// <summary>How long a message may be.</summary>
    public const int MaxLength = 4000;

    private Message(
        Guid id, Guid conversation, Side side, SubjectId? author, string text,
        Origin origin, bool isDraft, DateTimeOffset createdAt, DateTimeOffset? sentAt)
    {
        Id = id;
        Conversation = conversation;
        Side = side;
        Author = author;
        Text = text;
        Origin = origin;
        IsDraft = isDraft;
        CreatedAt = createdAt;
        SentAt = sentAt;
    }

    /// <summary>Which message.</summary>
    public Guid Id { get; }

    /// <summary>In which conversation.</summary>
    public Guid Conversation { get; }

    /// <summary>Whose side.</summary>
    public Side Side { get; }

    /// <summary>
    /// Who released it — or asked for the draft. <c>null</c> once that person's
    /// account was erased: the message stays in the other person's
    /// conversation, the author does not.
    /// </summary>
    public SubjectId? Author { get; private set; }

    /// <summary>The words.</summary>
    public string Text { get; private set; }

    /// <summary>Whether a model drafted it.</summary>
    public Origin Origin { get; }

    /// <summary>Whether it still waits for a human being to release it.</summary>
    public bool IsDraft { get; private set; }

    /// <summary>When it came into being.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it was sent — <c>null</c> for a draft.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>A message a human being sends — written themselves, or a model's draft they released.</summary>
    /// <param name="origin">
    /// <see cref="Origin.Agent"/> when the text came from a draft the sender
    /// released (the first approach is drafted in the scout, before a
    /// conversation exists). Released is always by a human being.
    /// </param>
    public static Message Write(
        Guid conversation, Side side, SubjectId author, string text, DateTimeOffset now,
        Origin origin = Origin.Human) =>
        new(Guid.CreateVersion7(), conversation, side, author, Checked(text),
            origin, isDraft: false, now, now);

    /// <summary>A draft a model wrote on request of <paramref name="requestedBy"/>.</summary>
    public static Message Draft(
        Guid conversation, Side side, SubjectId requestedBy, string text, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), conversation, side, requestedBy, Checked(text),
            Origin.Agent, isDraft: true, now, null);

    /// <summary>Back from the store.</summary>
    public static Message Restore(
        Guid id, Guid conversation, Side side, SubjectId? author, string text,
        Origin origin, bool isDraft, DateTimeOffset createdAt, DateTimeOffset? sentAt) =>
        new(id, conversation, side, author, text, origin, isDraft, createdAt, sentAt);

    /// <summary>
    /// A human being releases the draft — as it stands, or as they changed it.
    /// From here on it is their message.
    /// </summary>
    public void Release(SubjectId by, string? changedText, DateTimeOffset now)
    {
        if (!IsDraft)
        {
            throw new NotADraft();
        }

        if (changedText is not null)
        {
            Text = Checked(changedText);
        }

        Author = by;
        IsDraft = false;
        SentAt = now;
    }

    private static string Checked(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var trimmed = text.Trim();

        if (trimmed.Length == 0)
        {
            throw new EmptyMessage();
        }

        return trimmed.Length > MaxLength ? throw new MessageTooLong(MaxLength) : trimmed;
    }
}

/// <summary>Where conversations and their messages live.</summary>
/// <remarks>
/// The questions there are: this process's conversation, my conversations, the
/// company's conversations. "All conversations of THIS person", asked by a
/// company, does not exist.
/// </remarks>
public interface IConversationStore
{
    /// <summary>The conversation of a process, or <c>null</c>.</summary>
    Task<Conversation?> FindAsync(string kind, Guid reference, CancellationToken cancellationToken = default);

    /// <summary>A conversation by id, or <c>null</c>.</summary>
    Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new conversation.</summary>
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>Saves a changed conversation.</summary>
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>
    /// What <paramref name="side"/> sees: every sent message, and its OWN drafts —
    /// never the other side's. Oldest first.
    /// </summary>
    Task<IReadOnlyList<Message>> VisibleToAsync(
        Guid conversation, Side side, CancellationToken cancellationToken = default);

    /// <summary>The last sent messages, oldest first — the context for a draft.</summary>
    Task<IReadOnlyList<Message>> LastSentAsync(
        Guid conversation, int count, CancellationToken cancellationToken = default);

    /// <summary>A message by id, or <c>null</c>.</summary>
    Task<Message?> MessageAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Adds a message.</summary>
    Task AddAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Saves a changed message.</summary>
    Task SaveAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>Removes a message — a discarded draft leaves nothing.</summary>
    Task DeleteAsync(Message message, CancellationToken cancellationToken = default);

    /// <summary>The person's conversations, newest first.</summary>
    Task<IReadOnlyList<Conversation>> OfPersonAsync(SubjectId person, CancellationToken cancellationToken = default);

    /// <summary>The company's conversations, newest first.</summary>
    Task<IReadOnlyList<Conversation>> OfCompanyAsync(TenantId company, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the person: their conversations fall whole, and where they wrote
    /// for a company the author goes, the message stays.
    /// </summary>
    /// <returns>How many rows were deliberately kept — always zero.</returns>
    Task<int> EraseAsync(SubjectId person, CancellationToken cancellationToken = default);
}
