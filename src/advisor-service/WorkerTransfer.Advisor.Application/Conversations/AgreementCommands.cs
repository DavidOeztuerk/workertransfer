using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;

namespace WorkerTransfer.Advisor.Application.Conversations;

/// <summary>"I agree."</summary>
public sealed record AgreeCommand(Guid Id, SubjectId Subject) : ICommandMessage<ConversationView>;

/// <summary>
/// The person's agreement — and only that makes a handover possible.
/// </summary>
/// <remarks>
/// The agreement a case grows out of belongs to the human being it is about. A
/// conversation a company could lift into a transfer on its own would be an
/// invitation one cannot decline.
/// </remarks>
public sealed class AgreeHandler(
    IConversationStore store, IConsentGate gate, TimeProvider clock)
    : IRequestHandler<AgreeCommand, ConversationView>
{
    /// <inheritdoc />
    /// <remarks>
    /// <strong>The stage comes from the ledger here too.</strong> Leaving it out
    /// — say as <c>Stage.None</c>, because this step does not change it — would
    /// mean claiming "nothing released" in the answer immediately after somebody
    /// agreed. An answer that lies is what gets believed later.
    /// </remarks>
    /// <exception cref="NoConversation">Does not exist, or belongs to another human being.</exception>
    /// <exception cref="TransitionNotAllowed">Not from this state.</exception>
    public async Task<ConversationView> Handle(
        AgreeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await ReleaseStageHandler.Mine(
            store, request.Id, request.Subject, cancellationToken);

        conversation.Agree(request.Subject, clock.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        var stages = await gate.StagesAsync(
            [(conversation.Subject, conversation.Company)], cancellationToken);

        return new ConversationView(conversation, stages[0]);
    }
}

/// <summary>"Make a case out of it."</summary>
public sealed record HandOverCommand(Guid Id, TenantId Company) : ICommandMessage<Guid>;

/// <summary>
/// The handover to transfer-service — <strong>one call, not a second state
/// machine.</strong>
/// </summary>
/// <remarks>
/// <para>ADR-0037 decision 4: a conversation lives here as long as it is about
/// stages and mandates; only the agreement becomes a case. The triangle
/// consensus lives in transfer-service and is <em>not</em> rebuilt — whether the
/// market status is released to this company, whether the person is
/// approachable, whether the current employer's release is needed: the door
/// called here checks all of that.</para>
///
/// <para><strong>A refusal from there is not a 500.</strong> It is a statement
/// about the conditions of this case, and the endpoint turns it into 409.
/// Silence, on the other hand, is 503: "we do not know" is something other than
/// "cannot".</para>
///
/// <para><strong>Write only once it has worked.</strong> The state moves to
/// <c>handed_over</c> after the case really exists — the other way round this
/// side would say "handed over" and the other side nothing.</para>
/// </remarks>
public sealed class HandOverHandler(
    IConversationStore store, ICaseHandover handover, TimeProvider clock)
    : IRequestHandler<HandOverCommand, Guid>
{
    /// <inheritdoc />
    /// <exception cref="NoConversation">Does not exist, or belongs to another company.</exception>
    /// <exception cref="TransitionNotAllowed">The person has not agreed yet.</exception>
    /// <exception cref="HandoverRefused">transfer-service refuses.</exception>
    public async Task<Guid> Handle(HandOverCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await store.GetAsync(request.Id, cancellationToken);

        if (conversation is null || conversation.Company != request.Company)
        {
            throw new NoConversation();
        }

        if (conversation.State != ConversationState.Agreed)
        {
            throw new TransitionNotAllowed(conversation.State, "handed over");
        }

        var caseItem = await handover.HandOverAsync(
            conversation.Subject, conversation.Occasion, cancellationToken);

        conversation.HandOver(clock.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        return caseItem;
    }
}

/// <summary>"That's it."</summary>
/// <param name="Subject">Set when the person ends it.</param>
/// <param name="Company">Set when the company ends it.</param>
public sealed record EndCommand(Guid Id, SubjectId? Subject, TenantId? Company)
    : ICommandMessage<ConversationView>;

/// <summary>Both sides may stop, from every running state.</summary>
/// <remarks>
/// A procedure one cannot leave is not a procedure but a trap — the same line
/// stands in transfer-service, and it holds here just the same.
/// <para>
/// The stages stay where they are. An end is not a withdrawal: whoever also
/// wants to take back visibility revokes it — and that is a second, deliberate
/// step, not a side effect.
/// </para>
/// </remarks>
public sealed class EndHandler(
    IConversationStore store, IConsentGate gate, TimeProvider clock)
    : IRequestHandler<EndCommand, ConversationView>
{
    /// <inheritdoc />
    /// <remarks>
    /// The stage comes from the ledger — see <see cref="AgreeHandler"/>. An end
    /// is not a withdrawal, and the answer must not pretend it is.
    /// </remarks>
    /// <exception cref="NoConversation">Does not exist, or belongs to somebody else.</exception>
    /// <exception cref="TransitionNotAllowed">It is no longer running.</exception>
    public async Task<ConversationView> Handle(
        EndCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await store.GetAsync(request.Id, cancellationToken);

        var mine = conversation is not null
                   && ((request.Subject is { } subject && conversation.Subject == subject)
                       || (request.Company is { } company && conversation.Company == company));

        if (conversation is null || !mine)
        {
            throw new NoConversation();
        }

        conversation.End(clock.GetUtcNow());

        await store.SaveAsync(conversation, cancellationToken);

        var stages = await gate.StagesAsync(
            [(conversation.Subject, conversation.Company)], cancellationToken);

        return new ConversationView(conversation, stages[0]);
    }
}
