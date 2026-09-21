using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Markt;

namespace WorkerTransfer.Transfer.Application.Markt;

/// <summary>Den eigenen MarketStatus schreiben.</summary>
public sealed record SaveMarketStatusCommand(
    SubjectId Wer,
    Verfuegbarkeit Verfuegbarkeit,
    bool Beschaeftigt,
    string Notiz) : IBefehl<MarketStatus>;

/// <summary>Der eigene MarketStatus.</summary>
/// <remarks>
/// Nie <c>null</c>. Anders als beim Profile: dort ist „noch keins" ein leeres
/// Formular, hier ist „nichts gesagt" ein echter Zustand mit einer Bedeutung —
/// nicht verfügbar. Ein <c>null</c> würde die Oberfläche zwingen, sich eine
/// Voreinstellung auszudenken, und die Gefahr ist, dass sie sich die falsche
/// ausdenkt.
/// </remarks>
public sealed record MyMarketStatusQuery(SubjectId Wer) : IAbfrage<MarketStatus>;

/// <summary>Der MarketStatus einer anderen Person — wenn er freigegeben ist.</summary>
public sealed record VisibleMarketStatusQuery(SubjectId Wer, TenantId Firma)
    : IAbfrage<MarketStatus?>;

/// <summary>Schreibt und liest den MarketStatus.</summary>
public sealed class MarketCommands(
    IMarketStore store,
    IConsentGate tor,
    TimeProvider uhr) :
    IRequestHandler<SaveMarketStatusCommand, MarketStatus>,
    IRequestHandler<MyMarketStatusQuery, MarketStatus>,
    IRequestHandler<VisibleMarketStatusQuery, MarketStatus?>
{
    /// <inheritdoc />
    public async Task<MarketStatus> Handle(
        SaveMarketStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = uhr.GetUtcNow();
        var vorhanden = await store.GetAsync(request.Wer, cancellationToken);
        MarketStatus status;

        if (vorhanden is null)
        {
            status = MarketStatus.Lege_an(
                request.Wer, request.Verfuegbarkeit, request.Beschaeftigt,
                request.Notiz, now);
        }
        else
        {
            vorhanden.Aendere(
                request.Verfuegbarkeit, request.Beschaeftigt, request.Notiz, now);

            status = vorhanden;
        }

        await store.SaveAsync(status, cancellationToken);

        return status;
    }

    /// <inheritdoc />
    public async Task<MarketStatus> Handle(
        MyMarketStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await store.GetAsync(request.Wer, cancellationToken)
               ?? MarketStatus.Voreingestellt(request.Wer, uhr.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<MarketStatus?> Handle(
        VisibleMarketStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await store.GetAsync(request.Wer, cancellationToken);

        if (status is null)
        {
            // Kein Ledger-Aufruf für etwas, das es nicht gibt: ein unnötiger
            // Weg hin und zurück, und er meldete dem Ledger geratene
            // Kennungen.
            return null;
        }

        // ConsentGateSilent fliegt bewusst durch: der Endpunkt macht daraus
        // 503. Nicht vorhanden und nicht freigegeben sind dagegen von außen
        // dasselbe — hier zählt das doppelt, denn schon die Existenz der
        // Aussage „diese Person hört zu" kann Schaden anrichten.
        return await tor.MaySeeMarketAsync(request.Wer, request.Firma, cancellationToken)
            ? status
            : null;
    }
}
