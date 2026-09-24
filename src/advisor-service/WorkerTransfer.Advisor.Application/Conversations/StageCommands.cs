using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Advisor.Application.Messages;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Domain.Conversations;

namespace WorkerTransfer.Advisor.Application.Conversations;

/// <summary>"Release stage N to this company."</summary>
public sealed record ReleaseStageCommand(Guid Id, SubjectId Subject, Stage Target)
    : ICommandMessage<Stage>;

/// <summary>
/// Writes ledger events — and creates no stage column.
/// </summary>
/// <remarks>
/// <para><strong>This is decision 2 of ADR-0037 in one handler.</strong> A
/// release grants the existing visibilities for this one company; afterwards
/// the stage stands in the ledger and nowhere else. What does <em>not</em>
/// happen here is the actual point: nothing is written to the
/// conversation.</para>
///
/// <para><strong>Cumulative.</strong> Whoever releases stage 3 also grants 1
/// and 2 — otherwise the new capability would stand while the stage below it
/// is missing, and a read would still give 0. A stage is not a jump but a
/// state.</para>
///
/// <para><strong>Only the person, and the ledger enforces it.</strong> The
/// grant uses their token; the ledger is strictly self-administered and
/// answers everybody else with 403. The check here — is this conversation
/// yours? — is the second one, not the only one.</para>
///
/// <para><strong>And it takes effect at once.</strong> No cache, neither here
/// nor when reading (ADR-0013): the company's next request asks the ledger
/// anew.</para>
/// </remarks>
public sealed class ReleaseStageHandler(IConversationStore store, IConsentGate gate)
    : IRequestHandler<ReleaseStageCommand, Stage>
{
    /// <inheritdoc />
    /// <exception cref="NoConversation">Does not exist, or belongs to another human being.</exception>
    public async Task<Stage> Handle(
        ReleaseStageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await Mine(store, request.Id, request.Subject, cancellationToken);

        var capabilities = StageCapabilities.All
            .Where(stage => stage <= request.Target)
            .SelectMany(stage => StageCapabilities.Grants(stage, conversation.Company))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await gate.GrantAsync(capabilities, cancellationToken);

        return request.Target;
    }

    /// <summary>
    /// This human being's conversation, or 404.
    /// </summary>
    /// <remarks>
    /// "Belongs to somebody else" and "does not exist" answer alike: a
    /// difference would reveal that this conversation exists.
    /// </remarks>
    internal static async Task<Conversation> Mine(
        IConversationStore store, Guid id, SubjectId subject, CancellationToken cancellationToken)
    {
        var conversation = await store.GetAsync(id, cancellationToken);

        return conversation is null || conversation.Subject != subject
            ? throw new NoConversation()
            : conversation;
    }
}

/// <summary>"Take back stage N and everything above it."</summary>
public sealed record RevokeStageCommand(Guid Id, SubjectId Subject, Stage From)
    : ICommandMessage<Stage>;

/// <summary>
/// Revokes in the ledger — and the revocation takes effect on the next request.
/// </summary>
/// <remarks>
/// <para><strong>Open upwards.</strong> Whoever takes back stage 1 takes 2 and
/// 3 with it: a stage standing above one that fell away would be a permission
/// without a foundation — the CV visible, the profile not.</para>
///
/// <para><strong>The conversation stays.</strong> It only drops out of the
/// company's list (stage 0) and answers 404 there. The person keeps it in their
/// own list and can release again — a revocation that took the row with it
/// would be a one-way street.</para>
///
/// <para><strong>The reason is a shape.</strong> The ledger demands one,
/// because a revocation must be explainable; what this service writes is
/// "stage withdrawn" and never a sentence about a human being. Free text is
/// what a person writes about themselves — something they type, not something
/// a service invents for them.</para>
/// </remarks>
public sealed class RevokeStageHandler(IConversationStore store, IConsentGate gate)
    : IRequestHandler<RevokeStageCommand, Stage>
{
    /// <summary>What stands in the ledger as the reason.</summary>
    public const string Reason = "stage withdrawn";

    /// <inheritdoc />
    /// <exception cref="NoConversation">Does not exist, or belongs to another human being.</exception>
    public async Task<Stage> Handle(
        RevokeStageCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await ReleaseStageHandler.Mine(
            store, request.Id, request.Subject, cancellationToken);

        var capabilities = StageCapabilities.All
            .Where(stage => stage >= request.From)
            .SelectMany(stage => StageCapabilities.Grants(stage, conversation.Company))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await gate.RevokeAsync(capabilities, Reason, cancellationToken);

        // What still stands afterwards is said by the ledger — not by this
        // calculation. A stage computed here would be the column, only
        // transient.
        var stages = await gate.StagesAsync(
            [(conversation.Subject, conversation.Company)], cancellationToken);

        return stages[0];
    }
}
