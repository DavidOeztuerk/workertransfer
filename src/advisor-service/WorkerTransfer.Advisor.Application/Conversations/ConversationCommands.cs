using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Advisor.Application.Conversations;

/// <summary>There is nothing to see here.</summary>
/// <remarks>
/// <para><strong>One exit for three situations, and that is the core of this
/// service:</strong> the conversation does not exist; it exists but no longer
/// stands at any stage; the person has excluded this company. All three answer
/// 404, byte-identical apart from the correlation id.</para>
///
/// <para>A code or message of its own for "exists but does not show itself"
/// would be an oracle — and a "locked" hint would reveal exactly what this stage
/// has not released: that there is something.</para>
/// </remarks>
public sealed class NoConversation() : Exception("No such conversation");

// ---------------------------------------------------------------------------
// Open
// ---------------------------------------------------------------------------

/// <summary>A company opens a conversation.</summary>
public sealed record OpenConversationCommand(
    SubjectId Subject, TenantId Company, string? Occasion)
    : ICommandMessage<(ConversationView View, bool Created)>;

/// <summary>
/// Creates it — but only when stage 1 already stands and nobody has been
/// excluded.
/// </summary>
/// <remarks>
/// <para><strong>The order is the promise.</strong> The ledger first: for
/// somebody who has released nothing to this company there is nothing here —
/// the same 404 as for a human being who does not exist. Only then the mandate,
/// and an exclusion answers with the same 404 too. A code of its own would say
/// "you are on their list", and that is information about a human being given
/// to the very company they are protecting themselves from.</para>
///
/// <para><strong>One conversation per pair.</strong> If one is already running,
/// it comes back instead of a second one being created: two conversations
/// between the same two would mean the same capabilities and stand at two
/// states that drift apart.</para>
///
/// <para><strong>The person learns of it</strong> — through the content-free
/// outbox (ADR-0025) and in the same transaction as the conversation. The
/// notification names no company: it carries only a kind, and who it was stands
/// behind the sign-in in their own list.</para>
/// </remarks>
public sealed class OpenConversationHandler(
    IConversationStore conversations,
    IMandateStore mandateStore,
    IConsentGate gate,
    ICompanyLookup companies,
    IOutbox outbox,
    TimeProvider clock)
    : IRequestHandler<OpenConversationCommand, (ConversationView View, bool Created)>
{
    /// <summary>The word on the wire. The same one notification-service reads.</summary>
    public const string Kind = "advisor_conversation";

    /// <inheritdoc />
    /// <exception cref="NoConversation">Nothing released, or excluded.</exception>
    public async Task<(ConversationView, bool)> Handle(
        OpenConversationCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stages = await gate.StagesAsync([(request.Subject, request.Company)], cancellationToken);
        var stage = stages[0];

        if (stage < Stage.Profile)
        {
            throw new NoConversation();
        }

        var mandate = await mandateStore.GetAsync(request.Subject, cancellationToken);

        if (mandate is not null && await Excluded(mandate, request.Company, cancellationToken))
        {
            throw new NoConversation();
        }

        if (await conversations.GetRunningAsync(request.Subject, request.Company, cancellationToken)
            is { } running)
        {
            return (ConversationView.Build(running, stage, mandate, null), false);
        }

        var conversation = Conversation.Open(
            request.Subject, request.Company, request.Occasion, clock.GetUtcNow());

        await conversations.SaveAsync(conversation, cancellationToken);
        await outbox.NoteAsync(request.Subject, Kind, cancellationToken);

        return (ConversationView.Build(conversation, stage, mandate, null), true);
    }

    private async Task<bool> Excluded(
        Mandate mandate, TenantId company, CancellationToken cancellationToken)
    {
        if (mandate.ExcludedDomains.Count == 0)
        {
            // No exclusion means: do not even ask. A call to identity-service
            // for every opening would be a hop that decides nothing in almost
            // every case.
            return false;
        }

        var card = await companies.GetAsync(company, cancellationToken);

        return mandate.Excludes(card?.Domain);
    }
}

// ---------------------------------------------------------------------------
// Read
// ---------------------------------------------------------------------------

/// <summary>"Show me my conversations."</summary>
public sealed record MyConversationsQuery(SubjectId Subject)
    : IQueryMessage<IReadOnlyList<ConversationView>>;

/// <summary>The person sees all their conversations, the withdrawn ones too.</summary>
/// <remarks>
/// <strong>Without a stage filter</strong>, unlike the company list: whoever
/// took a stage back must be able to give it again. A list from which a
/// conversation vanished after the revocation would make the revocation a
/// one-way street.
/// <para>
/// The mandate does not come along: it is their own and lives under
/// <c>/advisor/me/mandate</c>. Delivering the same thing twice would be two
/// places where it later drifts apart.
/// </para>
/// </remarks>
public sealed class MyConversationsHandler(
    IConversationStore conversations, IConsentGate gate)
    : IRequestHandler<MyConversationsQuery, IReadOnlyList<ConversationView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationView>> Handle(
        MyConversationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var mine = await conversations.ForPersonAsync(request.Subject, cancellationToken);

        if (mine.Count == 0)
        {
            return [];
        }

        var stages = await gate.StagesAsync(
            [.. mine.Select(c => (c.Subject, c.Company))], cancellationToken);

        return [.. mine.Select((c, i) => new ConversationView(c, stages[i]))];
    }
}

/// <summary>"Show us our conversations."</summary>
public sealed record CompanyConversationsQuery(TenantId Company)
    : IQueryMessage<IReadOnlyList<ConversationView>>;

/// <summary>
/// The company sees what stands at stage 1 or higher — and nothing else.
/// </summary>
/// <remarks>
/// <para><strong>A conversation at stage 0 drops out of the list</strong>
/// instead of standing in it empty. A row without content would be the
/// "locked" hint in list form: it would say that this human being exists and
/// that they have withdrawn. Neither is the company's business.</para>
///
/// <para>And that is why there is <strong>no total</strong> (ADR-0026):
/// through the difference to the length it would reveal exactly the rows that
/// just dropped out.</para>
///
/// <para>The civil name is fetched <strong>only after</strong> the stage
/// answer, and only for the rows that carry stage 3. Nothing is looked up about
/// somebody who released nothing.</para>
/// </remarks>
public sealed class CompanyConversationsHandler(
    IConversationStore conversations,
    IMandateStore mandateStore,
    IConsentGate gate,
    IPersonLookup people)
    : IRequestHandler<CompanyConversationsQuery, IReadOnlyList<ConversationView>>
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationView>> Handle(
        CompanyConversationsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var theirs = await conversations.ForCompanyAsync(request.Company, cancellationToken);

        if (theirs.Count == 0)
        {
            return [];
        }

        var stages = await gate.StagesAsync(
            [.. theirs.Select(c => (c.Subject, c.Company))], cancellationToken);

        var views = new List<ConversationView>(theirs.Count);

        for (var i = 0; i < theirs.Count; i++)
        {
            if (stages[i] < Stage.Profile)
            {
                continue;
            }

            var mandate = await mandateStore.GetAsync(theirs[i].Subject, cancellationToken);

            var person = stages[i] >= Stage.Person
                ? await people.GetAsync(theirs[i].Subject, cancellationToken)
                : null;

            views.Add(ConversationView.Build(theirs[i], stages[i], mandate, person));
        }

        return views;
    }
}

/// <summary>"Show us this one conversation."</summary>
public sealed record CompanyConversationQuery(Guid Id, TenantId Company)
    : IQueryMessage<ConversationView>;

/// <summary>One — or 404, for each of the three reasons.</summary>
public sealed class CompanyConversationHandler(
    IConversationStore conversations,
    IMandateStore mandateStore,
    IConsentGate gate,
    IPersonLookup people)
    : IRequestHandler<CompanyConversationQuery, ConversationView>
{
    /// <inheritdoc />
    /// <exception cref="NoConversation">Does not exist, belongs to others, or stage 0.</exception>
    public async Task<ConversationView> Handle(
        CompanyConversationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await conversations.GetAsync(request.Id, cancellationToken);

        // "Belongs to another company" and "does not exist" answer alike: a
        // difference would reveal that this conversation exists.
        if (conversation is null || conversation.Company != request.Company)
        {
            throw new NoConversation();
        }

        var stages = await gate.StagesAsync(
            [(conversation.Subject, conversation.Company)], cancellationToken);

        if (stages[0] < Stage.Profile)
        {
            throw new NoConversation();
        }

        var mandate = await mandateStore.GetAsync(conversation.Subject, cancellationToken);

        var person = stages[0] >= Stage.Person
            ? await people.GetAsync(conversation.Subject, cancellationToken)
            : null;

        return ConversationView.Build(conversation, stages[0], mandate, person);
    }
}
