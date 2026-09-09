namespace WorkerTransfer.Jobs.Application.Ports;

/// <summary>Eine Transaktion um einen Befehl.</summary>
public interface IUnitOfWork
{
    /// <summary>Führt die Arbeit in einer Transaktion aus.</summary>
    Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default);
}
