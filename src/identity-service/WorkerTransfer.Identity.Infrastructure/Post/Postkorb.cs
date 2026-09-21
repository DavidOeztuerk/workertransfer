using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>One mail, waiting for the commit.</summary>
/// <param name="To">Where it goes.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Text">Its body.</param>
/// <param name="Recipient">
/// Whose account it is. For the log, so a failure does not write the address
/// into it — the audit allowlist excludes addresses for exactly this reason.
/// </param>
public sealed record OutgoingMail(
    string To, string Subject, string Text, SubjectId Recipient);

/// <summary>Collects mail during a request and sends it afterwards.</summary>
/// <remarks>
/// Scoped, so the tray belongs to one request and cannot leak into the next.
/// Both halves of the port are on one class because they are one tray; they are
/// two interfaces so a handler can only ever put something in.
/// </remarks>
public sealed class MailTray(
    IMailSender mailSender,
    IOptions<MailSettings> settings,
    ILogger<MailTray> log) : IMailTray, IMailTrayDispatch
{
    private readonly List<OutgoingMail> _pending = [];
    private readonly MailSettings _settings = settings.Value;

    /// <inheritdoc />
    public void ConfirmationLink(
        string to, SubjectId recipient, string plainTextToken, AccountLanguage language)
    {
        var baseUrl = _settings.WebAdresse.TrimEnd('/');
        var text = MailTexts.Confirmation(
            language, $"{baseUrl}/verify?token={plainTextToken}");

        _pending.Add(new OutgoingMail(to, text.Subject, text.Text, recipient));
    }

    /// <inheritdoc />
    public void DuplicateRegistration(string to, SubjectId recipient, AccountLanguage language)
    {
        var text = MailTexts.DuplicateRegistration(language);
        _pending.Add(new OutgoingMail(to, text.Subject, text.Text, recipient));
    }

    /// <inheritdoc />
    public void InvitationMail(
        string to, SubjectId inviter, string company, string plainTextToken,
        AccountLanguage language)
    {
        var baseUrl = _settings.WebAdresse.TrimEnd('/');
        var who = string.IsNullOrWhiteSpace(company)
            ? MailTexts.ACompany(language)
            : company;
        var text = MailTexts.InvitationMail(
            language, who, $"{baseUrl}/invitation?token={plainTextToken}");

        _pending.Add(new OutgoingMail(to, text.Subject, text.Text, inviter));
    }

    /// <inheritdoc />
    public void ErasureConfirmation(string to, SubjectId subject, AccountLanguage language)
    {
        var text = MailTexts.ErasureConfirmation(language);
        _pending.Add(new OutgoingMail(to, text.Subject, text.Text, subject));
    }

    /// <inheritdoc />
    public void News(string to, SubjectId subject, AccountLanguage language)
    {
        var baseUrl = _settings.WebAdresse.TrimEnd('/');
        var text = MailTexts.News(language, baseUrl);

        _pending.Add(new OutgoingMail(to, text.Subject, text.Text, subject));
    }

    /// <inheritdoc />
    public async Task SendCollectedAsync(CancellationToken cancellationToken = default)
    {
        // Taken out first: a send that throws must not leave the tray full for
        // whatever runs next in this scope.
        var toSend = _pending.ToArray();
        _pending.Clear();

        foreach (var mail in toSend)
        {
            try
            {
                await mailSender.SendAsync(mail, cancellationToken);
            }
            catch (Exception error)
            {
                log.LogError(
                    error, "Mail to account {Account} could not be delivered",
                    mail.Recipient);
            }
        }
    }
}
