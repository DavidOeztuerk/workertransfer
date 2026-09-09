namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>Eine Transaktion um einen Befehl.</summary>
/// <remarks>
/// Girder bringt kein Transaktions-Behavior mit. Gebraucht wird es hier, weil
/// ein Portfolio und seine Anhänge zusammengehören: eine Zeile ohne ihre Datei
/// oder eine Datei ohne ihre Zeile ist beides Müll, den niemand aufräumt.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Führt die Arbeit in einer Transaktion aus.</summary>
    Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default);
}
