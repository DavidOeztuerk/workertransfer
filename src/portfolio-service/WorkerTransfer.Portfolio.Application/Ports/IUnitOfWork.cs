namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>A transaction around a command.</summary>
/// <remarks>
/// Noelia brings no transaction behavior of its own. It is needed here because
/// a portfolio and its attachments belong together: a row without its file, or
/// a file without its row, is both rubbish that nobody clears away.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Runs the work in a transaction.</summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
