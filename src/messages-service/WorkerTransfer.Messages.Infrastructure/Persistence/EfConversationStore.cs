using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Messages.Infrastructure.Persistence;

/// <summary>Conversations and messages in Postgres.</summary>
public sealed class EfConversationStore(MessagesDbContext context) : IConversationStore
{
    /// <inheritdoc />
    public async Task<Conversation?> FindAsync(
        string kind, Guid reference, CancellationToken cancellationToken = default)
    {
        var row = await context.Conversations.FirstOrDefaultAsync(
            entry => entry.Kind == kind && entry.Reference == reference, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Conversations.FirstOrDefaultAsync(
            entry => entry.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public Task AddAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        context.Conversations.Add(new ConversationRow
        {
            Id = conversation.Id,
            Kind = conversation.Kind,
            Reference = conversation.Reference,
            SubjectId = conversation.Person.Value,
            TenantId = conversation.Company.Value,
            CreatedAt = conversation.CreatedAt.UtcDateTime,
            LastMessageAt = conversation.LastMessageAt.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        // The tracked entry first: a conversation opened in this very
        // transaction is not in the database yet, and a query would not find it.
        var row = context.Conversations.Local.FirstOrDefault(entry => entry.Id == conversation.Id)
            ?? await context.Conversations
                .AsTracking()
                .FirstOrDefaultAsync(entry => entry.Id == conversation.Id, cancellationToken);

        if (row is not null)
        {
            row.LastMessageAt = conversation.LastMessageAt.UtcDateTime;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> VisibleToAsync(
        Guid conversation, Side side, CancellationToken cancellationToken = default)
    {
        var word = Word(side);

        // The other side's drafts are filtered IN the query — a draft that never
        // leaves the database cannot be shown by a mistake further up.
        return [.. (await context.Messages
                .Where(entry => entry.ConversationId == conversation
                                && (!entry.IsDraft || entry.Side == word))
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Message>> LastSentAsync(
        Guid conversation, int count, CancellationToken cancellationToken = default)
    {
        var rows = await context.Messages
            .Where(entry => entry.ConversationId == conversation && !entry.IsDraft)
            .OrderByDescending(entry => entry.SentAt)
            .ThenByDescending(entry => entry.Id)
            .Take(count)
            .ToListAsync(cancellationToken);

        rows.Reverse();
        return [.. rows.Select(ToAggregate)];
    }

    /// <inheritdoc />
    public async Task<Message?> MessageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Messages.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public Task AddAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        context.Messages.Add(new MessageRow
        {
            Id = message.Id,
            ConversationId = message.Conversation,
            Side = Word(message.Side),
            AuthorId = message.Author?.Value,
            Text = message.Text,
            Origin = Word(message.Origin),
            IsDraft = message.IsDraft,
            CreatedAt = message.CreatedAt.UtcDateTime,
            SentAt = message.SentAt?.UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SaveAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        // AsTracking, because this changes the row. Without it the release
        // would answer 200 and the draft would stay a draft (the defect
        // `PUT /resumes/…/as-cv` once cost).
        var row = await context.Messages
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == message.Id, cancellationToken);

        if (row is null)
        {
            return;
        }

        row.Text = message.Text;
        row.AuthorId = message.Author?.Value;
        row.IsDraft = message.IsDraft;
        row.SentAt = message.SentAt?.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var row = await context.Messages
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == message.Id, cancellationToken);

        if (row is not null)
        {
            context.Messages.Remove(row);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> OfPersonAsync(
        SubjectId person, CancellationToken cancellationToken = default) =>
        [.. (await context.Conversations
                .Where(entry => entry.SubjectId == person.Value)
                .OrderByDescending(entry => entry.LastMessageAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ToAggregate)];

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> OfCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default) =>
        [.. (await context.Conversations
                .Where(entry => entry.TenantId == company.Value)
                .OrderByDescending(entry => entry.LastMessageAt)
                .ThenByDescending(entry => entry.Id)
                .ToListAsync(cancellationToken))
            .Select(ToAggregate)];

    /// <inheritdoc />
    public async Task<int> EraseAsync(SubjectId person, CancellationToken cancellationToken = default)
    {
        var conversations = await context.Conversations
            .AsTracking()
            .Where(entry => entry.SubjectId == person.Value)
            .ToListAsync(cancellationToken);

        var ids = conversations.Select(entry => entry.Id).ToList();

        // Explicitly, not only by the foreign key's cascade: the unit of work
        // saves through the change tracker, and a message it does not know of
        // would rely on the database's ON DELETE alone — one setting away from
        // staying behind.
        var messages = await context.Messages
            .AsTracking()
            .Where(entry => ids.Contains(entry.ConversationId))
            .ToListAsync(cancellationToken);

        context.Messages.RemoveRange(messages);
        context.Conversations.RemoveRange(conversations);

        // Where they wrote for a company, in somebody else's conversation: the
        // message stays with that other person, the author goes.
        var authored = await context.Messages
            .AsTracking()
            .Where(entry => entry.AuthorId == person.Value && !ids.Contains(entry.ConversationId))
            .ToListAsync(cancellationToken);

        foreach (var row in authored)
        {
            if (row.IsDraft)
            {
                // An unreleased draft is nobody's message yet.
                context.Messages.Remove(row);
            }
            else
            {
                row.AuthorId = null;
            }
        }

        // And the notes about this human being — they carry the id.
        var notes = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(entry => entry.UserId == person.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(notes);

        return 0;
    }

    private static Conversation ToAggregate(ConversationRow row) =>
        Conversation.Restore(
            row.Id, row.Kind, row.Reference, new SubjectId(row.SubjectId), new TenantId(row.TenantId),
            At(row.CreatedAt), At(row.LastMessageAt));

    private static Message ToAggregate(MessageRow row) =>
        Message.Restore(
            row.Id, row.ConversationId, ReadSide(row.Side),
            row.AuthorId is { } author ? new SubjectId(author) : null,
            row.Text, row.Origin == "agent" ? Origin.Agent : Origin.Human,
            row.IsDraft, At(row.CreatedAt), row.SentAt is { } sent ? At(sent) : null);

    /// <summary>The word a side travels and is stored under.</summary>
    public static string Word(Side side) => side == Side.Person ? "person" : "company";

    /// <summary>The word an origin is stored under.</summary>
    public static string Word(Origin origin) => origin == Origin.Agent ? "agent" : "human";

    private static Side ReadSide(string word) => word == "company" ? Side.Company : Side.Person;

    private static DateTimeOffset At(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
