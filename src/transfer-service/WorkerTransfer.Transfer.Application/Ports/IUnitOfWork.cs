namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>A transaction around a command.</summary>
public interface IUnitOfWork
{
    /// <summary>Runs the work in a transaction.</summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
