namespace WorkerTransfer.Assessment.Application.Ports;

/// <summary>A transaction around a command.</summary>
public interface IUnitOfWork
{
    /// <summary>Runs the work and commits it as one.</summary>
    Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
