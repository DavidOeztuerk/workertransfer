namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>A transaction around a command.</summary>
/// <remarks>
/// Re-entrant by contract: a nested call joins the open transaction instead of
/// opening a second one.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Runs the work inside a transaction and commits it together.</summary>
    /// <param name="work">What is to run inside the transaction.</param>
    /// <param name="cancellationToken">Cancels and rolls back.</param>
    /// <returns>What <paramref name="work"/> answered.</returns>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
