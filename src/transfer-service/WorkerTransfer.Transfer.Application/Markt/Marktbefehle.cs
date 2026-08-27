using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Transfer.Application.Nachrichten;
using WorkerTransfer.Transfer.Application.Ports;
using WorkerTransfer.Transfer.Domain.Markt;

namespace WorkerTransfer.Transfer.Application.Markt;

/// <summary>Den eigenen Marktstatus schreiben.</summary>
public sealed record MarktstatusSichernBefehl(
    SubjectId Wer,
    Verfuegbarkeit Verfuegbarkeit,
    bool Beschaeftigt,
    string Notiz) : IBefehl<Marktstatus>;

/// <summary>Der eigene Marktstatus.</summary>
/// <remarks>
/// Nie <c>null</c>. Anders als beim Profil: dort ist „noch keins" ein leeres
/// Formular, hier ist „nichts gesagt" ein echter Zustand mit einer Bedeutung —
/// nicht verfügbar. Ein <c>null</c> würde die Oberfläche zwingen, sich eine
/// Voreinstellung auszudenken, und die Gefahr ist, dass sie sich die falsche
/// ausdenkt.
/// </remarks>
public sealed record MeinMarktstatusAbfrage(SubjectId Wer) : IAbfrage<Marktstatus>;

/// <summary>Der Marktstatus einer anderen Person — wenn er freigegeben ist.</summary>
public sealed record SichtbarerMarktstatusAbfrage(SubjectId Wer, TenantId Firma)
    : IAbfrage<Marktstatus?>;

/// <summary>Schreibt und liest den Marktstatus.</summary>
public sealed class Marktbefehle(
    IMarktspeicher speicher,
    IEinwilligungstor tor,
    TimeProvider uhr) :
    IRequestHandler<MarktstatusSichernBefehl, Marktstatus>,
    IRequestHandler<MeinMarktstatusAbfrage, Marktstatus>,
    IRequestHandler<SichtbarerMarktstatusAbfrage, Marktstatus?>
{
    /// <inheritdoc />
    public async Task<Marktstatus> Handle(
        MarktstatusSichernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var jetzt = uhr.GetUtcNow();
        var vorhanden = await speicher.HoleAsync(request.Wer, cancellationToken);
        Marktstatus status;

        if (vorhanden is null)
        {
            status = Marktstatus.Lege_an(
                request.Wer, request.Verfuegbarkeit, request.Beschaeftigt,
                request.Notiz, jetzt);
        }
        else
        {
            vorhanden.Aendere(
                request.Verfuegbarkeit, request.Beschaeftigt, request.Notiz, jetzt);

            status = vorhanden;
        }

        await speicher.SichereAsync(status, cancellationToken);

        return status;
    }

    /// <inheritdoc />
    public async Task<Marktstatus> Handle(
        MeinMarktstatusAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await speicher.HoleAsync(request.Wer, cancellationToken)
               ?? Marktstatus.Voreingestellt(request.Wer, uhr.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<Marktstatus?> Handle(
        SichtbarerMarktstatusAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = await speicher.HoleAsync(request.Wer, cancellationToken);

        if (status is null)
        {
            // Kein Ledger-Aufruf für etwas, das es nicht gibt: ein unnötiger
            // Weg hin und zurück, und er meldete dem Ledger geratene
            // Kennungen.
            return null;
        }

        // EinwilligungSchweigt fliegt bewusst durch: der Endpunkt macht daraus
        // 503. Nicht vorhanden und nicht freigegeben sind dagegen von außen
        // dasselbe — hier zählt das doppelt, denn schon die Existenz der
        // Aussage „diese Person hört zu" kann Schaden anrichten.
        return await tor.DarfMarktSehenAsync(request.Wer, request.Firma, cancellationToken)
            ? status
            : null;
    }
}
