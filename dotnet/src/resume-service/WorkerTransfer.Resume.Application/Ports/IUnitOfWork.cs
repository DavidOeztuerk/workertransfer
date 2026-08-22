namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>One transaction around one command.</summary>
/// <remarks>
/// Girder brings no transaction behaviour, and this service needs one for two
/// reasons. The trail has to commit together with the change it records. And
/// the outbox row has to commit together with the request that caused it — a
/// notification written separately could exist while the request was rolled
/// back, and somebody would be told a company asked for their résumé when none
/// had (ADR-0025).
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
