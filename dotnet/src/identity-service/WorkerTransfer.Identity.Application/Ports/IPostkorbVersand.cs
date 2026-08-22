namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Empties the tray.</summary>
/// <remarks>
/// Split from <see cref="IPostkorb"/> so a handler can only ever queue. Whoever
/// holds the queueing side cannot decide when it goes out, and that decision —
/// after the commit, never before — is the one thing about mail here that is
/// not a matter of taste.
/// </remarks>
public interface IPostkorbVersand
{
    /// <summary>Sends everything queued in this request, and clears it.</summary>
    Task VersendeGesammeltesAsync(CancellationToken cancellationToken = default);
}
