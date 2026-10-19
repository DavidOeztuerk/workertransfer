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

/// <summary>A person's unsolicited application to a company (VISION-FRAGEN B2).</summary>
/// <param name="Person">Who applies — from the token, acting as herself.</param>
/// <param name="Company">To which company.</param>
/// <param name="Text">Her first message.</param>
public sealed record OpenInitiativeCommand(SubjectId Person, TenantId Company, string? Text)
    : ICommandMessage<(Conversation Conversation, Message Message)>;

/// <summary>
/// Releases her profile to the company, opens the one conversation between
/// the two, sends, and tells the company's members.
/// </summary>
/// <remarks>
/// <para><strong>Sending IS the release</strong> — the same as applying to an
/// advert. The text is checked FIRST, so a refused message leaves no release
/// behind; the release is written before the conversation, so a silent
/// ledger leaves no conversation the company could not open.</para>
///
/// <para><strong>A company with no members is no company</strong> — 404, and
/// no release is written to a tenant nobody stands behind.</para>
/// </remarks>
public sealed class OpenInitiativeHandler(
    IReleaseWriter releases,
    ICompanyMemberLookup members,
    IConversationStore store,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<OpenInitiativeCommand, (Conversation, Message)>
{
    /// <inheritdoc />
    public async Task<(Conversation, Message)> Handle(OpenInitiativeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Message.Check(request.Text);

        var recipients = await members.MembersAsync(request.Company, cancellationToken);

        if (recipients.Count == 0)
        {
            throw new NoConversation();
        }

        await releases.GrantProfileAsync(request.Person, request.Company, cancellationToken);

        var now = clock.GetUtcNow();
        var conversation = await Conversations.OpenAsync(
            store, ProcessKinds.Initiative, ProcessKinds.InitiativeReference(request.Company, request.Person),
            new ProcessParties(request.Person, request.Company, string.Empty), now, cancellationToken);

        var message = Message.Write(conversation.Id, Side.Person, request.Person, request.Text ?? string.Empty, now);
        await store.AddAsync(message, cancellationToken);
        conversation.Touch(now);
        await store.SaveAsync(conversation, cancellationToken);

        foreach (var member in recipients)
        {
            await outbox.NoteAsync(member, NotificationKinds.Received, cancellationToken);
        }

        return (conversation, message);
    }
}
