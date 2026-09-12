namespace WorkerTransfer.Assessment.Application.Ports;

/// <summary>Eine Klammer um einen Befehl.</summary>
public interface IUnitOfWork
{
    /// <summary>Führt die Arbeit aus und schreibt sie gemeinsam fest.</summary>
    Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default);
}
