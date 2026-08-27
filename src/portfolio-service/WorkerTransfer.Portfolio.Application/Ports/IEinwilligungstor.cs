using Girder.Core.Identity;

namespace WorkerTransfer.Portfolio.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet.</summary>
/// <remarks>
/// Ein eigener Fehler und kein <c>false</c>: „ich weiß es nicht" ist etwas
/// anderes als „nein", und die beiden zu verwechseln heißt, ein Portfolio zu
/// verbergen, das freigegeben ist — oder eines zu zeigen, das es nicht ist.
/// </remarks>
public sealed class EinwilligungSchweigt(string meldung) : Exception(meldung);

/// <summary>Fragt den Consent-Ledger, jedes Mal.</summary>
/// <remarks>
/// Nichts wird zwischengespeichert (ADR-0013). Ein Widerruf muss beim nächsten
/// Lesen wirken — ein Cache hier wäre kein Geschwindigkeitsdetail, sondern ein
/// gebrochenes Versprechen.
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>Ob dieses Portfolio gezeigt werden darf.</summary>
    /// <remarks>
    /// <b>Dieselbe Fähigkeit deckt die Anhänge mit ab</b> (ADR-0021). Ein
    /// zweites Tor für Dateien wäre eine zweite Wahrheit, und ein Widerruf
    /// wirkte dann auf die Beschreibung, aber nicht auf die Arbeitsprobe.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<bool> DarfSehenAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
