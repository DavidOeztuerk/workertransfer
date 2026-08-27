namespace WorkerTransfer.Consent.Application.Ports;

/// <summary>Removes the free text from everything one person left behind.</summary>
/// <remarks>
/// The one exception to append-only, and it stands here rather than on
/// <c>IConsentLedger</c> on purpose: a <c>ClearReasons</c> method on the ledger
/// would be open to every caller, and the promise that nobody can rewrite
/// history would be gone for all of them. This port is reachable from the
/// erasure and nowhere else, and its name says what it does.
/// <para>
/// What it clears is <c>reason</c> and <c>metadata</c>, in
/// <c>consent_events</c> and in <c>audit_events</c>. What it keeps is the rest
/// of every row: the ledger is the <em>proof</em> that the erasure happened,
/// and deleting it would make the promise unprovable (ADR-0027 §5).
/// </para>
/// </remarks>
public interface IFreitextraeumung
{
    /// <param name="subject">Whose rows.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RaeumeAsync(Guid subject, CancellationToken cancellationToken = default);
}
