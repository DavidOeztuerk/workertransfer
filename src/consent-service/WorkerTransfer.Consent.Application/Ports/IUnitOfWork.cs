namespace WorkerTransfer.Consent.Application.Ports;

/// <summary>One transaction around one command.</summary>
/// <remarks>
/// Girder brings no transaction behaviour, and the reason this service needs
/// one is the audit trail: the consent fact and the row that records it land
/// together or neither lands (ADR-0012). There is no state "consent recorded
/// but audit lost", and no orphaned audit row for a write that failed.
/// <para>
/// The erasure needs it for a harder reason still: a closing fact per
/// capability and the clearing of every free text are one decision.
/// </para>
/// <para>
/// Reentrant by contract: a nested call joins the transaction already open
/// rather than opening a second one.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <param name="arbeit">What to run inside the transaction.</param>
    /// <param name="cancellationToken">Cancels the work and rolls back.</param>
    /// <returns>Whatever <paramref name="arbeit"/> answered.</returns>
    Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default);
}
