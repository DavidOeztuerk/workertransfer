using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Nachrichten;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Application.Loeschung;

/// <summary>Delete this account, and everything anybody holds about the person.</summary>
/// <remarks>
/// Self only, and there is <em>no reason field</em>. Demanding a justification
/// from somebody who wants to leave is a lever against them — and the free text
/// would be the one thing that afterwards had to be deleted again (ADR-0027).
/// </remarks>
public sealed record LoeschungVerlangenBefehl(SubjectId Wer) : IBefehl<Loeschergebnis>;

/// <summary>What asking produced.</summary>
public enum Loeschergebnis
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
public sealed class LoeschungVerlangenHandler(
    ILoeschbestand bestand,
    IOutbox outbox,
    TimeProvider uhr)
    : IRequestHandler<LoeschungVerlangenBefehl, Loeschergebnis>
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
        .. Loeschempfaenger.Fremde.Select(Loeschempfaenger.Art),
        Loeschempfaenger.Schlussnachricht,
        Loeschempfaenger.Identitaet
    ];

    /// <inheritdoc />
    public async Task<Loeschergebnis> Handle(
        LoeschungVerlangenBefehl request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await bestand.LaeuftBereitsAsync(request.Wer, cancellationToken))
        {
            return Loeschergebnis.LaeuftBereits;
        }

        // Immediately and visibly: every session revoked, the account disabled.
        // From this moment nothing happens under this name any more, even while
        // the cascade is still running (ADR-0027 §6).
        await bestand.SperreAsync(request.Wer, uhr.GetUtcNow(), cancellationToken);

        // In the SAME transaction as that change (ADR-0025): if it goes
        // through, the intents stand; if it is rolled back, they are gone too.
        // There is no state "locked out, but nobody was told".
        foreach (var art in Absichten)
        {
            await outbox.VermerkeAsync(request.Wer, art, cancellationToken);
        }

        return Loeschergebnis.Angenommen;
    }
}
