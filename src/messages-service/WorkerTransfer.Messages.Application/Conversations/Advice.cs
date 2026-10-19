using MediatR;
using WorkerTransfer.Messages.Application.Messages;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Domain.Conversations;

namespace WorkerTransfer.Messages.Application.Conversations;

/// <summary>The advisor works for the person — a company has none (VISION-FRAGEN D4).</summary>
public sealed class AdvisorIsThePersons() : Exception("the advisor works for the person");

/// <summary>Advice for the person about one of her conversations (ADR-0049).</summary>
/// <remarks>A query: it stores nothing and changes nothing.</remarks>
public sealed record AdviseQuery(string Kind, Guid Reference, Caller Caller) : IQueryMessage<string>;

/// <summary>Reads the conversation and her own facts, and asks the model.</summary>
public sealed class AdviseHandler(
    ConversationAccess access,
    IConversationStore store,
    IOwnFactsLookup facts,
    IAdvisor advisor) : IRequestHandler<AdviseQuery, string>
{
    /// <inheritdoc />
    public async Task<string> Handle(AdviseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Caller.Company is not null)
        {
            throw new AdvisorIsThePersons();
        }

        var (parties, _) = await access.ResolveAsync(
            request.Kind, request.Reference, request.Caller, cancellationToken);

        var conversation = await store.FindAsync(request.Kind, request.Reference, cancellationToken);
        var history = conversation is null
            ? []
            : await store.LastSentAsync(conversation.Id, ComposeReplyHandler.HistoryLength, cancellationToken);

        var own = await facts.GetAsync(cancellationToken);

        var context = new AdviceContext(
            [.. history.Select(message => new HistoryEntry(message.Side, message.Text))],
            parties.PersonName,
            own,
            "de");

        return await advisor.AdviseAsync(context, request.Caller.Subject, cancellationToken);
    }
}
