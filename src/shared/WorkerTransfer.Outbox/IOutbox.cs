using Girder.Core.Identity;

namespace WorkerTransfer.Outbox;

/// <summary>Records an intent in the caller's running transaction.</summary>
/// <remarks>
/// No commit of its own and no session of its own — that is the entire point.
/// Written independently, the notification could exist while the change was
/// rolled back: somebody would be told their transfer was accepted, and the
/// database would hold nothing of the sort.
/// </remarks>
public interface IOutbox
{
    /// <param name="empfaenger">Whom the intent is about.</param>
    /// <param name="art">
    /// What is meant. A short, stable label — never a message, never a reason.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The id of the row, for a caller that wants to watch it.</returns>
    Task<Guid> VermerkeAsync(
        SubjectId empfaenger,
        string art,
        CancellationToken cancellationToken = default);
}

/// <summary>What delivers — HTTP, SMTP, whatever.</summary>
public interface IZustellung
{
    /// <summary>
    /// Delivers one intent, or throws.
    /// </summary>
    /// <remarks>
    /// Throwing is a supported answer and for the erasure it is the important
    /// one: a delivery that swallows its failures would make
    /// <see cref="OutboxZeile.DeliveredAt"/> a lie, and the completeness proof
    /// of ADR-0027 would prove nothing.
    /// </remarks>
    /// <exception cref="NochNichtException">The row's turn has not come.</exception>
    Task ZustelleAsync(
        SubjectId empfaenger,
        string art,
        CancellationToken cancellationToken = default);
}

/// <summary>"Not yet" — not a failure, and therefore not a spent attempt.</summary>
/// <remarks>
/// Some intents have a load-bearing order among themselves: the final notice of
/// an erasure may only go out once every recipient has acknowledged, and the
/// account may only fall after that (ADR-0027 §6). That order is
/// <em>enforced</em>, not advised — the dispatcher puts a row back instead of
/// delivering it.
/// <para>
/// Its own exception and not simply a failure, because a failure raises
/// <c>attempts</c> and writes <c>last_error</c>. Orderly waiting would then look
/// exactly like a broken recipient, and under an attempt ceiling the order would
/// even strangle the delivery — the row would count as given up because it
/// waited its turn.
/// </para>
/// </remarks>
public sealed class NochNichtException(string grund) : Exception(grund);
