using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>Sends over SMTP.</summary>
/// <remarks>
/// <c>System.Net.Mail</c> rather than a mail package: this service sends two
/// plain-text mails, and every library that would do it also brings templating,
/// three cloud providers and an HTTP client into an image that needs none of
/// them.
/// </remarks>
public sealed class SmtpVersender(IOptions<Postsettings> einstellungen) : IVersender
{
    private readonly Postsettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task SendeAsync(
        AusgehendePost post,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        using var client = new SmtpClient(_einstellungen.Host, _einstellungen.Port)
        {
            EnableSsl = _einstellungen.Tls,
            Timeout = 10_000
        };

        // Only sign in where credentials are set. Mailpit and most development
        // catchers know no AUTH at all, and offering it unasked fails the send
        // before it starts.
        if (!string.IsNullOrEmpty(_einstellungen.Benutzer)
            && !string.IsNullOrEmpty(_einstellungen.Passwort))
        {
            client.Credentials = new System.Net.NetworkCredential(
                _einstellungen.Benutzer, _einstellungen.Passwort);
        }

        using var nachricht = new MailMessage(_einstellungen.Absender, post.An)
        {
            Subject = post.Betreff,
            Body = post.Text
        };

        await client.SendMailAsync(nachricht, cancellationToken);
    }
}
