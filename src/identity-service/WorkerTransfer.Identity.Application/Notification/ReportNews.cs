using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Identity.Application.Messages;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Notification;

/// <summary>"Tell this person there is something new."</summary>
/// <remarks>
/// Comes from notification-service, which decided <em>whether</em> anything
/// goes out. The command carries <strong>nothing but an id</strong> — no kind,
/// no text: the address lives here, the sentence too, and a caller that could
/// contribute something would be the beginning of "just this one line more".
/// </remarks>
public sealed record ReportNewsCommand(SubjectId Subject) : ICommandMessage<bool>;

/// <summary>Resolves the address and puts the one mail into the tray.</summary>
/// <remarks>
/// Answers whether anything was put in — for tests and the log. The endpoint
/// does <strong>not</strong> pass that on: otherwise it would be an oracle
/// about whether this person exists and whether their account is confirmed.
/// </remarks>
public sealed class ReportNewsHandler(IUserRepository users, IMailTray mailTray)
    : IRequestHandler<ReportNewsCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        ReportNewsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var account = await users.FindByIdAsync(request.Subject, cancellationToken);

        // An unconfirmed account gets no mail about cases — the address has
        // not been proven to be theirs yet.
        if (account is null || account.Status is not AccountStatus.Active)
        {
            return false;
        }

        mailTray.News(account.Email, account.Id, account.AccountLanguage);

        return true;
    }
}
