using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Infrastructure.Persistence;

/// <summary>The conversations in Postgres.</summary>
public sealed class EfConversationStore(AdvisorDbContext context) : IConversationStore
{
    /// <inheritdoc />
    public async Task<Conversation?> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Conversations
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default)
    {
        var running = ConversationStates.Running.Select(ConversationStates.Word).ToArray();

        var row = await context.Conversations
            .Where(entry => entry.SubjectId == subject.Value
                              && entry.TenantId == company.Value
                              && running.Contains(entry.State))
            .OrderByDescending(entry => entry.OpenedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : ToDomain(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Conversation conversation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        // AsTracking, because this changes things: without it the row would come
        // back detached, the assignments would go nowhere, and the caller would
        // still get its 200.
        var row = await context.Conversations
            .AsTracking()
            .FirstOrDefaultAsync(entry => entry.Id == conversation.Id, cancellationToken);

        if (row is null)
        {
            context.Conversations.Add(new ConversationRow
            {
                Id = conversation.Id,
                SubjectId = conversation.Subject.Value,
                TenantId = conversation.Company.Value,
                State = ConversationStates.Word(conversation.State),
                Note = conversation.Occasion,
                OpenedAt = conversation.OpenedAt.UtcDateTime,
                UpdatedAt = conversation.UpdatedAt.UtcDateTime
            });

            return;
        }

        row.State = ConversationStates.Word(conversation.State);
        row.UpdatedAt = conversation.UpdatedAt.UtcDateTime;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var rows = await context.Conversations
            .Where(entry => entry.SubjectId == subject.Value)
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToDomain)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default)
    {
        var rows = await context.Conversations
            .Where(entry => entry.TenantId == company.Value)
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(ToDomain)];
    }

    /// <inheritdoc />
    public async Task<int> DeleteAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var conversations = await context.Conversations
            .AsTracking()
            .Where(entry => entry.SubjectId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Conversations.RemoveRange(conversations);

        var mandates = await context.Mandates
            .AsTracking()
            .Where(entry => entry.Id == subject.Value)
            .ToListAsync(cancellationToken);

        context.Mandates.RemoveRange(mandates);

        // And the outbox entries ABOUT this human being. They carry their id
        // and are therefore personal — an erasure that takes only the
        // conversations would leave in every backup that this person existed
        // (ADR-0027).
        var entries = await context.Set<OutboxRow>()
            .AsTracking()
            .Where(entry => entry.UserId == subject.Value)
            .ToListAsync(cancellationToken);

        context.Set<OutboxRow>().RemoveRange(entries);

        // Always 0. This service knows no retention case: a conversation is a
        // relation, not a record, and a mandate belongs to the person.
        return 0;
    }

    private static Conversation ToDomain(ConversationRow row) =>
        Conversation.Restore(
            row.Id,
            new SubjectId(row.SubjectId),
            new TenantId(row.TenantId),
            State(row.State),
            row.Note,
            new DateTimeOffset(row.OpenedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));

    /// <summary>The word back into a state.</summary>
    /// <remarks>
    /// An unknown word is a programming error and gets loud: falling back to
    /// "talking" quietly would reopen an ended conversation.
    /// </remarks>
    private static ConversationState State(string word) => word switch
    {
        "talking" => ConversationState.Talking,
        "agreed" => ConversationState.Agreed,
        "handed_over" => ConversationState.HandedOver,
        "ended" => ConversationState.Ended,
        _ => throw new InvalidOperationException($"'{word}' is not a conversation state.")
    };
}
