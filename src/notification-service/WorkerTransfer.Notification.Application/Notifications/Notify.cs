using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Messages;
using WorkerTransfer.Notification.Application.Ports;
using WorkerTransfer.Notification.Domain.Notifications;

namespace WorkerTransfer.Notification.Application.Notifications;

/// <summary>"Tell this person there is something new."</summary>
public sealed record NotifyCommand(SubjectId Subject, NotificationKind Kind)
    : ICommandMessage<bool>;

/// <summary>Creates the entry and decides whether mail goes out.</summary>
/// <remarks>
/// The two halves are deliberately differently strict:
/// <list type="bullet">
/// <item>The <strong>entry in the inbox always comes into being</strong>. It
/// lies behind the sign-in, where who reads is checked; throttling it would
/// mean concealing from somebody that something happened. <em>One</em> kind is
/// the exception, and for the opposite reason: with <c>profile_discovered</c>
/// an entry per hit would itself be the disclosure nobody should have — a
/// counter over one's own visibility (ADR-0033). There a daily cap applies, and
/// it takes the entry with it.</item>
/// <item>The <strong>mail is throttled and can be switched off</strong>. It
/// lands in an inbox that need not belong to the person alone.</item>
/// </list>
/// <para>
/// The return value says whether something was sent — for tests and the log.
/// The endpoint does <strong>not</strong> pass that on: the caller must not be
/// able to derive from the answer whether the person exists or whether they
/// want mail.
/// </para>
/// </remarks>
public sealed class NotifyHandler(
    IPreferenceStore preferences,
    IInboxStore inbox,
    TimeProvider clock) : IRequestHandler<NotifyCommand, bool>
{
    /// <inheritdoc />
    public async Task<bool> Handle(
        NotifyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = clock.GetUtcNow();

        // THE CAP OF ITS OWN PER KIND, and it strikes the ENTRY, not only the
        // mail. For `profile_discovered` an entry per hit would be a counter
        // over one's own visibility — by a detour exactly the register ADR-0033
        // does not let come into being. So both fall away here, and silently:
        // the caller learns nothing about it.
        if (NotificationKinds.DailyCap(request.Kind) is { } cap
            && await inbox.ExistedSinceAsync(
                request.Subject, request.Kind, now - cap, cancellationToken))
        {
            return false;
        }

        await inbox.AppendAsync(
            InboxEntry.Create(request.Subject, request.Kind, now), cancellationToken);

        var preference = await preferences.GetAsync(request.Subject, cancellationToken)
                     ?? NotificationPreference.Default(request.Subject);

        if (!preference.MayNow(request.Kind, now))
        {
            return false;
        }

        // The throttle is set BEFORE anything is sent, and in the same
        // transaction. If delivery fails afterwards this message is lost — that
        // is the right direction: rather one mail too few than a loop that
        // tries again at every event.
        preference.MarkSent(now);
        await preferences.SaveAsync(preference, cancellationToken);

        return true;
    }
}
