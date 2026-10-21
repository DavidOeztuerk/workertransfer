using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Messages;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Messages.Application.Conversations;

/// <summary>The words this service writes into the outbox.</summary>
public static class NotificationKinds
{
    /// <summary>"A message arrived" — the ninth kind (ADR-0047). Names nobody.</summary>
    public const string Received = "message_received";
}

/// <summary>Who asks, and in which capacity.</summary>
/// <param name="Subject">The signed-in human being.</param>
/// <param name="Company">The company they act for — <c>null</c> when they act as themselves.</param>
public sealed record Caller(SubjectId Subject, TenantId? Company);

/// <summary>No conversation this caller may see — 404, whatever the reason.</summary>
/// <remarks>
/// "Not your process", "no such process" and "a draft of the other side" are
/// one answer. Three would tell a stranger which application ids exist and
/// tell a person that the company is drafting something.
/// </remarks>
public sealed class NoConversation() : Exception("No such conversation");

/// <summary>What the caller's side sees.</summary>
public sealed record ConversationView(
    string Kind, Guid Reference, Side Side, IReadOnlyList<Message> Messages);

/// <summary>Who may open a conversation, and on which side.</summary>
/// <remarks>
/// <para><strong>An application:</strong> its own service is asked, with the
/// caller's token, at every request (ADR-0047).</para>
///
/// <para><strong>An approach:</strong> the parties stand in the conversation —
/// the company opened it, after the ledger said yes. The COMPANY side asks the
/// ledger again at every request: a person who takes their profile back is no
/// longer written to, from the next request on (ADR-0013). The PERSON side does
/// not: what was written to somebody stays readable to them after they
/// revoked.</para>
///
/// <para>Every refusal is the same 404.</para>
/// </remarks>
public sealed class ConversationAccess(
    IProcessLookup lookup, IConversationStore store, IConsentGate gate)
{
    /// <summary>The parties and the caller's side.</summary>
    /// <exception cref="NoConversation">The caller is not a party, or the process does not exist.</exception>
    public async Task<(ProcessParties Parties, Side Side)> ResolveAsync(
        string kind, Guid reference, Caller caller, CancellationToken cancellationToken)
    {
        if (!ProcessKinds.IsKnown(kind))
        {
            throw new NoConversation();
        }

        // An approach and an unsolicited application: the parties stand in the
        // conversation, and the company side asks the ledger every time.
        if (ProcessKinds.StandsOnTheLedger(kind))
        {
            var conversation = await store.FindAsync(kind, reference, cancellationToken)
                ?? throw new NoConversation();
            var parties = new ProcessParties(conversation.Person, conversation.Company, string.Empty);

            if (caller.Company is { } acting)
            {
                return acting == conversation.Company
                       && await gate.MaySeeAsync(conversation.Person, acting, cancellationToken)
                    ? (parties, Side.Company)
                    : throw new NoConversation();
            }

            return caller.Subject == conversation.Person
                ? (parties, Side.Person)
                : throw new NoConversation();
        }

        var found = await lookup.PartiesAsync(kind, reference, cancellationToken)
            ?? throw new NoConversation();

        // The capacity decides the side, not who the human being is: acting for
        // the company, even the applicant's own colleague writes as the company.
        if (caller.Company is { } company)
        {
            return found.Company == company ? (found, Side.Company) : throw new NoConversation();
        }

        return found.Person == caller.Subject ? (found, Side.Person) : throw new NoConversation();
    }
}

/// <summary>Who hears of a message.</summary>
internal static class Sides
{
    /// <summary>Who hears of a message <paramref name="from"/> sent.</summary>
    public static async Task<IReadOnlyList<SubjectId>> RecipientsAsync(
        ICompanyMemberLookup members, ProcessParties parties, Side from, SubjectId writer,
        CancellationToken cancellationToken)
    {
        if (from == Side.Company)
        {
            return [parties.Person];
        }

        // A company has no mailbox; its members do (the same as application_received).
        var all = await members.MembersAsync(parties.Company, cancellationToken);
        return [.. all.Where(member => member != writer)];
    }
}

// ---------------------------------------------------------------------------
// Reading

/// <summary>The conversation of a process, as the caller's side sees it.</summary>
public sealed record ConversationQuery(string Kind, Guid Reference, Caller Caller)
    : IQueryMessage<ConversationView>;

/// <summary>Sent messages and the own side's drafts — never the other side's.</summary>
/// <remarks>
/// A conversation that does not exist yet is an empty one, not a 404: whoever
/// may see the process may start it.
/// </remarks>
public sealed class ConversationHandler(ConversationAccess access, IConversationStore store)
    : IRequestHandler<ConversationQuery, ConversationView>
{
    /// <inheritdoc />
    public async Task<ConversationView> Handle(ConversationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, side) = await access.ResolveAsync(
            request.Kind, request.Reference, request.Caller, cancellationToken);

        var conversation = await store.FindAsync(request.Kind, request.Reference, cancellationToken);

        var messages = conversation is null
            ? []
            : await store.VisibleToAsync(conversation.Id, side, cancellationToken);

        return new ConversationView(request.Kind, request.Reference, side, messages);
    }
}

/// <summary>"My conversations" — the person's, or the company's.</summary>
public sealed record MyConversationsQuery(Caller Caller) : IQueryMessage<IReadOnlyList<Conversation>>;

/// <summary>Read from the stored parties; opening one asks the process again.</summary>
public sealed class MyConversationsHandler(IConversationStore store)
    : IRequestHandler<MyConversationsQuery, IReadOnlyList<Conversation>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Conversation>> Handle(MyConversationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Caller.Company is { } company
            ? store.OfCompanyAsync(company, cancellationToken)
            : store.OfPersonAsync(request.Caller.Subject, cancellationToken);
    }
}

// ---------------------------------------------------------------------------
// Writing

/// <summary>A human being writes and sends.</summary>
public sealed record WriteCommand(string Kind, Guid Reference, Caller Caller, string? Text)
    : ICommandMessage<Message>;

/// <summary>Sends, and tells the other side in the same transaction.</summary>
public sealed class WriteHandler(
    ConversationAccess access,
    ICompanyMemberLookup members,
    IConversationStore store,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<WriteCommand, Message>
{
    /// <inheritdoc />
    public async Task<Message> Handle(WriteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parties, side) = await access.ResolveAsync(
            request.Kind, request.Reference, request.Caller, cancellationToken);

        var now = clock.GetUtcNow();
        var conversation = await Conversations.OpenAsync(
            store, request.Kind, request.Reference, parties, now, cancellationToken);

        var message = Message.Write(
            conversation.Id, side, request.Caller.Subject, request.Text ?? string.Empty, now);

        await store.AddAsync(message, cancellationToken);
        conversation.Touch(now);
        await store.SaveAsync(conversation, cancellationToken);

        foreach (var recipient in await Sides.RecipientsAsync(
                     members, parties, side, request.Caller.Subject, cancellationToken))
        {
            await outbox.NoteAsync(recipient, NotificationKinds.Received, cancellationToken);
        }

        return message;
    }
}

internal static class Conversations
{
    public static async Task<Conversation> OpenAsync(
        IConversationStore store, string kind, Guid reference, ProcessParties parties,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await store.FindAsync(kind, reference, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var opened = Conversation.Open(kind, reference, parties.Person, parties.Company, now);
        await store.AddAsync(opened, cancellationToken);
        return opened;
    }
}

// ---------------------------------------------------------------------------
// Drafts (VISION-FRAGEN D2: a human being releases everything an agent writes)

/// <summary>Asks the model for a reply — reads, writes nothing.</summary>
/// <remarks>
/// A query on purpose: the model call takes seconds, and inside a command it
/// would hold a database transaction open for all of them. The draft is
/// stored afterwards by <see cref="SaveDraftCommand"/>.
/// </remarks>
public sealed record ComposeReplyQuery(string Kind, Guid Reference, Caller Caller)
    : IQueryMessage<string>;

/// <summary>Builds the context from this conversation's sent messages and nothing else.</summary>
public sealed class ComposeReplyHandler(
    ConversationAccess access, IConversationStore store, IReplyDrafter drafter)
    : IRequestHandler<ComposeReplyQuery, string>
{
    /// <summary>How many sent messages the model reads.</summary>
    public const int HistoryLength = 20;

    /// <inheritdoc />
    public async Task<string> Handle(ComposeReplyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parties, side) = await access.ResolveAsync(
            request.Kind, request.Reference, request.Caller, cancellationToken);

        var conversation = await store.FindAsync(request.Kind, request.Reference, cancellationToken);

        var history = conversation is null
            ? []
            : await store.LastSentAsync(conversation.Id, HistoryLength, cancellationToken);

        var context = new ReplyContext(
            side,
            [.. history.Select(message => new HistoryEntry(message.Side, message.Text))],
            parties.PersonName,
            "de");

        return await drafter.DraftAsync(
            context, request.Caller.Subject, request.Caller.Company, cancellationToken);
    }
}

/// <summary>Stores a model's text as a draft of the caller's side.</summary>
public sealed record SaveDraftCommand(string Kind, Guid Reference, Caller Caller, string Text)
    : ICommandMessage<Message>;

/// <summary>A draft tells nobody: the other side learns of it never.</summary>
public sealed class SaveDraftHandler(
    ConversationAccess access, IConversationStore store, TimeProvider clock)
    : IRequestHandler<SaveDraftCommand, Message>
{
    /// <inheritdoc />
    public async Task<Message> Handle(SaveDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parties, side) = await access.ResolveAsync(
            request.Kind, request.Reference, request.Caller, cancellationToken);

        var now = clock.GetUtcNow();
        var conversation = await Conversations.OpenAsync(
            store, request.Kind, request.Reference, parties, now, cancellationToken);

        var draft = Message.Draft(conversation.Id, side, request.Caller.Subject, request.Text, now);
        await store.AddAsync(draft, cancellationToken);

        return draft;
    }
}

/// <summary>A human being releases a draft — as it stands, or changed.</summary>
public sealed record ReleaseDraftCommand(Guid Message, Caller Caller, string? Text)
    : ICommandMessage<Message>;

/// <summary>From here on it is a sent message, and the other side hears of it.</summary>
public sealed class ReleaseDraftHandler(
    ConversationAccess access,
    ICompanyMemberLookup members,
    IConversationStore store,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<ReleaseDraftCommand, Message>
{
    /// <inheritdoc />
    public async Task<Message> Handle(ReleaseDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (draft, conversation, parties) = await Drafts.OwnAsync(
            access, store, request.Message, request.Caller, cancellationToken);

        var now = clock.GetUtcNow();
        draft.Release(request.Caller.Subject, request.Text, now);
        await store.SaveAsync(draft, cancellationToken);

        conversation.Touch(now);
        await store.SaveAsync(conversation, cancellationToken);

        foreach (var recipient in await Sides.RecipientsAsync(
                     members, parties, draft.Side, request.Caller.Subject, cancellationToken))
        {
            await outbox.NoteAsync(recipient, NotificationKinds.Received, cancellationToken);
        }

        return draft;
    }
}

/// <summary>A draft is thrown away and leaves nothing.</summary>
public sealed record DiscardDraftCommand(Guid Message, Caller Caller) : ICommandMessage<bool>;

/// <summary>Deletes the row — no "discarded" state, no count of discarded drafts.</summary>
public sealed class DiscardDraftHandler(ConversationAccess access, IConversationStore store)
    : IRequestHandler<DiscardDraftCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(DiscardDraftCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (draft, _, _) = await Drafts.OwnAsync(
            access, store, request.Message, request.Caller, cancellationToken);

        await store.DeleteAsync(draft, cancellationToken);
        return true;
    }
}

internal static class Drafts
{
    /// <summary>
    /// The draft, if it is the caller's side's — otherwise the same 404 as for
    /// a draft that does not exist.
    /// </summary>
    public static async Task<(Message Draft, Conversation Conversation, ProcessParties Parties)> OwnAsync(
        ConversationAccess access, IConversationStore store, Guid id, Caller caller,
        CancellationToken cancellationToken)
    {
        var message = await store.MessageAsync(id, cancellationToken) ?? throw new NoConversation();
        var conversation = await store.GetAsync(message.Conversation, cancellationToken)
            ?? throw new NoConversation();

        var (parties, side) = await access.ResolveAsync(
            conversation.Kind, conversation.Reference, caller, cancellationToken);

        if (side != message.Side || !message.IsDraft)
        {
            // The other side's draft does not exist for this caller; a sent
            // message of the own side is no draft — answered as one 404 so a
            // guessed id says nothing.
            throw new NoConversation();
        }

        return (message, conversation, parties);
    }
}
