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
public sealed class SmtpMailSender(IOptions<MailSettings> settings) : IMailSender
{
    private readonly MailSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task SendAsync(
        OutgoingMail mail,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.Tls,
            Timeout = 10_000
        };

        // Only sign in where credentials are set. Mailpit and most development
        // catchers know no AUTH at all, and offering it unasked fails the send
        // before it starts.
        if (!string.IsNullOrEmpty(_settings.Account)
            && !string.IsNullOrEmpty(_settings.Password))
        {
            client.Credentials = new System.Net.NetworkCredential(
                _settings.Account, _settings.Password);
        }

        using var message = new MailMessage(_settings.Absender, mail.To)
        {
            Subject = mail.Subject,
            Body = mail.Text
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
