using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Nachrichten;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Application.Benachrichtigungen;

/// <summary>Das eigene Inbox.</summary>
public sealed record MyInboxQuery(SubjectId Wer) : IAbfrage<IReadOnlyList<Eingang>>;

/// <summary>Alles als gelesen markieren.</summary>
public sealed record AllesGelesenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Die eigenen Einstellungen.</summary>
public sealed record MyPreferencesQuery(SubjectId Wer) : IAbfrage<NotificationPreference>;

/// <summary>Die eigenen Einstellungen schreiben.</summary>
public sealed record WuenscheSchreibenBefehl(
    SubjectId Wer, IReadOnlyDictionary<Benachrichtigungsart, bool> Schalter)
    : IBefehl<NotificationPreference>;

/// <summary>Inbox und Einstellungen — beides nur für einen selbst.</summary>
/// <remarks>
/// Es gibt keine Route, über die jemand das Inbox eines anderen läse, und
/// keine Kennung im Pfad: wessen Inbox es ist, steht im geprüften Token. Ein
/// Endpunkt <c>/notifications/{subjectId}</c> wäre ein Orakel darüber, ob und
/// wie oft jemandem etwas passiert.
/// </remarks>
public sealed class Inbox(
    IInboxStore eingaenge,
    IPreferenceStore preferences,
    TimeProvider uhr) :
    IRequestHandler<MyInboxQuery, IReadOnlyList<Eingang>>,
    IRequestHandler<AllesGelesenBefehl, int>,
    IRequestHandler<MyPreferencesQuery, NotificationPreference>,
    IRequestHandler<WuenscheSchreibenBefehl, NotificationPreference>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Eingang>> Handle(
        MyInboxQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return eingaenge.ForPersonAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> Handle(AllesGelesenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return eingaenge.ReadAllAsync(request.Wer, uhr.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NotificationPreference> Handle(
        MyPreferencesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nie `null`: wer nichts eingestellt hat, hat alles an — und die
        // Oberfläche soll sich keine Voreinstellung ausdenken müssen.
        return await preferences.GetAsync(request.Wer, cancellationToken)
               ?? NotificationPreference.Voreingestellt(request.Wer);
    }

    /// <inheritdoc />
    public async Task<NotificationPreference> Handle(
        WuenscheSchreibenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var preference = await preferences.GetAsync(request.Wer, cancellationToken)
                     ?? NotificationPreference.Voreingestellt(request.Wer);

        foreach (var (kind, gewollt) in request.Schalter)
        {
            preference.Set(kind, gewollt);
        }

        await preferences.SaveAsync(preference, cancellationToken);

        return preference;
    }
}
