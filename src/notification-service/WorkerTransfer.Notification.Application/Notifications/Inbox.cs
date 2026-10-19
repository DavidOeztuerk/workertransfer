using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Messages;
using WorkerTransfer.Notification.Domain.Notifications;

namespace WorkerTransfer.Notification.Application.Notifications;

/// <summary>One's own inbox.</summary>
public sealed record MyInboxQuery(SubjectId Subject) : IQueryMessage<IReadOnlyList<InboxEntry>>;

/// <summary>Mark everything as read.</summary>
public sealed record ReadAllCommand(SubjectId Subject) : ICommandMessage<int>;

/// <summary>One's own settings.</summary>
public sealed record MyPreferencesQuery(SubjectId Subject) : IQueryMessage<NotificationPreference>;

/// <summary>Write one's own settings.</summary>
public sealed record WritePreferencesCommand(
    SubjectId Subject, IReadOnlyDictionary<NotificationKind, bool> Switches)
    : ICommandMessage<NotificationPreference>;

/// <summary>Inbox and settings — both only for oneself.</summary>
/// <remarks>
/// There is no route over which somebody would read another person's inbox,
/// and no id in the path: whose inbox it is stands in the verified token. An
/// endpoint <c>/notifications/{subjectId}</c> would be an oracle about whether
/// and how often something happens to somebody.
/// </remarks>
public sealed class Inbox(
    IInboxStore inbox,
    IPreferenceStore preferences,
    TimeProvider clock) :
    IRequestHandler<MyInboxQuery, IReadOnlyList<InboxEntry>>,
    IRequestHandler<ReadAllCommand, int>,
    IRequestHandler<MyPreferencesQuery, NotificationPreference>,
    IRequestHandler<WritePreferencesCommand, NotificationPreference>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEntry>> Handle(
        MyInboxQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return inbox.ForPersonAsync(request.Subject, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> Handle(ReadAllCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return inbox.ReadAllAsync(request.Subject, clock.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NotificationPreference> Handle(
        MyPreferencesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Never `null`: whoever has set nothing has everything on — and the
        // interface should not have to invent a default.
        return await preferences.GetAsync(request.Subject, cancellationToken)
               ?? NotificationPreference.Default(request.Subject);
    }

    /// <inheritdoc />
    public async Task<NotificationPreference> Handle(
        WritePreferencesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var preference = await preferences.GetAsync(request.Subject, cancellationToken)
                     ?? NotificationPreference.Default(request.Subject);

        foreach (var (kind, wanted) in request.Switches)
        {
            preference.Set(kind, wanted);
        }

        await preferences.SaveAsync(preference, cancellationToken);

        return preference;
    }
}
