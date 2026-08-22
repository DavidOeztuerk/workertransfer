namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>Puts one mail on its way.</summary>
/// <remarks>
/// Its own seam inside the infrastructure so a test can run the whole
/// registration — tray, timing, wording — without an SMTP server, and still
/// through the real code that decides what goes out.
/// </remarks>
public interface IVersender
{
    /// <param name="post">What to send.</param>
    /// <param name="cancellationToken">Cancels the send.</param>
    Task SendeAsync(AusgehendePost post, CancellationToken cancellationToken = default);
}
