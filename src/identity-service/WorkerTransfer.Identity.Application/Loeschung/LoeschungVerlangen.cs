using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Application.Erasure;

/// <summary>Delete this account, and everything anybody holds about the person.</summary>
/// <remarks>
/// Self only, and there is <em>no reason field</em>. Demanding a justification
/// from somebody who wants to leave is a lever against them — and the free text
/// would be the one thing that afterwards had to be deleted again (ADR-0027).
/// </remarks>
public sealed record RequestErasureCommand(SubjectId Wer) : IBefehl<ErasureResult>;

/// <summary>What asking produced.</summary>
public enum ErasureResult
{
    /// <summary>Accepted. The cascade is running.</summary>
    Angenommen,

    /// <summary>
    /// It was already running. Not an error.
    /// </summary>
    /// <remarks>
    /// Pressing twice must not start a second cascade: eight more rows would be
    /// eight more deliveries for a thing that happens once.
    /// </remarks>
    LaeuftBereits
}

/// <summary>Takes the request, and makes it visible at once.</summary>
public sealed class RequestErasureHandler(
    IErasureLedger bestand,
    IOutbox outbox,
    TimeProvider uhr)
    : IRequestHandler<RequestErasureCommand, ErasureResult>
{
    /// <summary>
    /// The twelve rows, in the order they fall due.
    /// </summary>
    /// <remarks>
    /// The order is <em>enforced by the dispatcher</em>, not by this list — all
    /// twelve come into being in the same instant and in the same transaction.
    /// </remarks>
    public static IReadOnlyList<string> Absichten { get; } =
    [
        .. ErasureRecipient.Fremde.Select(ErasureRecipient.Art),
        ErasureRecipient.Schlussnachricht,
        ErasureRecipient.Identitaet
    ];

    /// <inheritdoc />
    public async Task<ErasureResult> Handle(
        RequestErasureCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await bestand.IsAlreadyRunningAsync(request.Wer, cancellationToken))
        {
            return ErasureResult.LaeuftBereits;
        }

        // Immediately and visibly: every session revoked, the account disabled.
        // From this moment nothing happens under this name any more, even while
        // the cascade is still running (ADR-0027 §6).
        await bestand.LockAsync(request.Wer, uhr.GetUtcNow(), cancellationToken);

        // In the SAME transaction as that change (ADR-0025): if it goes
        // through, the intents stand; if it is rolled back, they are gone too.
        // There is no state "locked out, but nobody was told".
        foreach (var kind in Absichten)
        {
            await outbox.NoteAsync(request.Wer, kind, cancellationToken);
        }

        return ErasureResult.Angenommen;
    }
}
