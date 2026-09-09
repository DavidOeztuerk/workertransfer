namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>One transaction around one command.</summary>
/// <remarks>
/// Girder brings no transaction behaviour, and the reason this service needs
/// one is the audit trail: an entry that commits separately from the change it
/// records can survive a rollback or be missing after a commit, and either way
/// it states something that did not happen. The erasure cascade needs it for a
/// harder reason still — nine outbox rows and the disabled account are one
/// decision or they are nothing (ADR-0027).
/// <para>
/// Reentrant by contract: a nested call joins the transaction already open
/// rather than opening a second one, so a handler that dispatches another
/// command does not deadlock against itself.
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
