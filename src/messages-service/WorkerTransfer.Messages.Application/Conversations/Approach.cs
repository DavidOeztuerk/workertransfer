using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Messages;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Messages.Application.Conversations;

/// <summary>A company's first message to a person (ADR-0048, VISION-FRAGEN B2).</summary>
/// <param name="Company">Who approaches — from the token.</param>
/// <param name="Sender">The recruiter who releases it.</param>
/// <param name="Person">Whom.</param>
/// <param name="Text">The words, as released.</param>
/// <param name="Drafted">
/// Whether a model drafted the words (the scout's approach draft). Released is
/// always by the sender; this only records where the words came from, for the
/// company's own side to see (ADR-0047, J2).
/// </param>
public sealed record OpenApproachCommand(
    TenantId Company, SubjectId Sender, SubjectId Person, string? Text, bool Drafted)
    : ICommandMessage<(Conversation Conversation, Message Message)>;

/// <summary>
/// Asks the ledger, opens (or continues) the one conversation between this
/// company and this person, sends, and tells the person.
/// </summary>
/// <remarks>
/// <para><strong>The role is checked before this runs</strong>, at the
/// endpoint: a first contact needs at least a <c>recruiter</c>.</para>
///
/// <para><strong>Nobody who released nothing can be approached</strong> — 404,
/// the same answer as for a person who does not exist. A different one would
/// tell a stranger that the id belongs to somebody.</para>
/// </remarks>
public sealed class OpenApproachHandler(
    IConsentGate gate,
    IConversationStore store,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<OpenApproachCommand, (Conversation, Message)>
{
    /// <inheritdoc />
    public async Task<(Conversation, Message)> Handle(OpenApproachCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await gate.MaySeeAsync(request.Person, request.Company, cancellationToken))
        {
            throw new NoConversation();
        }

        var now = clock.GetUtcNow();
        var reference = ProcessKinds.ApproachReference(request.Company, request.Person);
        var conversation = await Conversations.OpenAsync(
            store, ProcessKinds.Approach, reference,
            new ProcessParties(request.Person, request.Company, string.Empty), now, cancellationToken);

        var message = Message.Write(
            conversation.Id, Side.Company, request.Sender, request.Text ?? string.Empty, now,
            request.Drafted ? Origin.Agent : Origin.Human);

        await store.AddAsync(message, cancellationToken);
        conversation.Touch(now);
        await store.SaveAsync(conversation, cancellationToken);

        await outbox.NoteAsync(request.Person, NotificationKinds.Received, cancellationToken);

        return (conversation, message);
    }
}
