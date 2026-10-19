namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>A transaction around a command.</summary>
/// <remarks>
/// Noelia brings no transaction scope, and the reason this service needs one is
/// the audit trail: an entry that commits separately from the change it records
/// survives a rollback or is missing after a commit — and in both cases claims
/// something that did not happen. For the erasure it holds harder: the row falls
/// and the trail records it, or nothing happens.
/// <para>
/// Re-entrant by contract: a nested call joins the open transaction instead of
/// opening a second one.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <param name="work">What is to run inside the transaction.</param>
    /// <param name="cancellationToken">Cancels and rolls back.</param>
    /// <returns>What <paramref name="work"/> answered.</returns>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
