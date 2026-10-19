using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Messages.Application.Messages;
using WorkerTransfer.Messages.Domain.Audit;
using WorkerTransfer.Messages.Domain.Conversations;

namespace WorkerTransfer.Messages.Application.Erasure;

/// <summary>Erases everything this service holds about a person.</summary>
public sealed record ErasePersonCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>Their conversations fall whole — the company's messages too.</summary>
/// <remarks>
/// <para><strong>No retention case.</strong> The company's messages in the
/// person's conversation are about the person; keeping them would keep the
/// person (ADR-0027, ADR-0047).</para>
///
/// <para><strong>A member who erases their account</strong> wrote in other
/// people's conversations. Those stay — they belong to that other person's
/// process — and only the author goes.</para>
/// </remarks>
public sealed class ErasePersonHandler(
    IConversationStore store,
    IAuditTrail trail,
    TimeProvider clock)
    : IRequestHandler<ErasePersonCommand, int>
{
    /// <inheritdoc />
    /// <returns>How much was deliberately kept. Always zero.</returns>
    public async Task<int> Handle(ErasePersonCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var kept = await store.EraseAsync(request.Subject, cancellationToken);

        // In the same transaction as the erasure: both commit, or neither.
        // THAT it ran, never what it deleted.
        await trail.AppendOnceAsync(
            new AuditEntry(AuditAction.Erased, request.Subject, clock.GetUtcNow()),
            cancellationToken);

        return kept;
    }
}
