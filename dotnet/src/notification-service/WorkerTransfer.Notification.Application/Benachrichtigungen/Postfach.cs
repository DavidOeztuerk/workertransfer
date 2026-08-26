using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Notification.Application.Nachrichten;
using WorkerTransfer.Notification.Domain.Benachrichtigungen;

namespace WorkerTransfer.Notification.Application.Benachrichtigungen;

/// <summary>Das eigene Postfach.</summary>
public sealed record MeinPostfachAbfrage(SubjectId Wer) : IAbfrage<IReadOnlyList<Eingang>>;

/// <summary>Alles als gelesen markieren.</summary>
public sealed record AllesGelesenBefehl(SubjectId Wer) : IBefehl<int>;

/// <summary>Die eigenen Einstellungen.</summary>
public sealed record MeineWuenscheAbfrage(SubjectId Wer) : IAbfrage<Benachrichtigungswunsch>;

/// <summary>Die eigenen Einstellungen schreiben.</summary>
public sealed record WuenscheSchreibenBefehl(
    SubjectId Wer, IReadOnlyDictionary<Benachrichtigungsart, bool> Schalter)
    : IBefehl<Benachrichtigungswunsch>;

/// <summary>Postfach und Einstellungen — beides nur für einen selbst.</summary>
/// <remarks>
/// Es gibt keine Route, über die jemand das Postfach eines anderen läse, und
/// keine Kennung im Pfad: wessen Postfach es ist, steht im geprüften Token. Ein
/// Endpunkt <c>/notifications/{subjectId}</c> wäre ein Orakel darüber, ob und
/// wie oft jemandem etwas passiert.
/// </remarks>
public sealed class Postfach(
    IEingangsspeicher eingaenge,
    IWunschspeicher wuensche,
    TimeProvider uhr) :
    IRequestHandler<MeinPostfachAbfrage, IReadOnlyList<Eingang>>,
    IRequestHandler<AllesGelesenBefehl, int>,
    IRequestHandler<MeineWuenscheAbfrage, Benachrichtigungswunsch>,
    IRequestHandler<WuenscheSchreibenBefehl, Benachrichtigungswunsch>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Eingang>> Handle(
        MeinPostfachAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return eingaenge.FuerPersonAsync(request.Wer, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> Handle(AllesGelesenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return eingaenge.LiesAllesAsync(request.Wer, uhr.GetUtcNow(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Benachrichtigungswunsch> Handle(
        MeineWuenscheAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Nie `null`: wer nichts eingestellt hat, hat alles an — und die
        // Oberfläche soll sich keine Voreinstellung ausdenken müssen.
        return await wuensche.HoleAsync(request.Wer, cancellationToken)
               ?? Benachrichtigungswunsch.Voreingestellt(request.Wer);
    }

    /// <inheritdoc />
    public async Task<Benachrichtigungswunsch> Handle(
        WuenscheSchreibenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var wunsch = await wuensche.HoleAsync(request.Wer, cancellationToken)
                     ?? Benachrichtigungswunsch.Voreingestellt(request.Wer);

        foreach (var (art, gewollt) in request.Schalter)
        {
            wunsch.Setze(art, gewollt);
        }

        await wuensche.SichereAsync(wunsch, cancellationToken);

        return wunsch;
    }
}
