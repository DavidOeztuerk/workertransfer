using Girder.Core.Identity;
using WorkerTransfer.Advisor.Domain.Gespraeche;

namespace WorkerTransfer.Advisor.Application.Ports;

/// <summary>Der Ledger hat nicht geantwortet — wir wissen es schlicht nicht.</summary>
/// <remarks>
/// Ausdrücklich <em>kein</em> <c>Stufe.Keine</c>. „Ich weiss es nicht" ist etwas
/// anderes als „nichts freigegeben", und die beiden gleich zu behandeln hiesse,
/// einen Ausfall in eine Aussage über einen Menschen zu verwandeln. Der
/// Endpunkt macht daraus 503 — nicht 404, denn das wäre die Behauptung, es gebe
/// nichts, und nicht „zeigen", denn das wäre die umgekehrte (ADR-0020 §3).
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class EinwilligungSchweigt(string grund) : Exception(grund);

/// <summary>Der Ledger hat die Freigabe abgelehnt.</summary>
/// <remarks>
/// Getrennt vom Schweigen, weil es etwas anderes ist: der Ledger hat
/// geantwortet, und zwar mit Nein. Das passiert genau dann, wenn jemand für
/// einen fremden Menschen freigeben wollte — der Ledger verwaltet sich strikt
/// selbst.
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags, nie sein Inhalt.</param>
public sealed class EinwilligungAbgelehnt(string grund) : Exception(grund);

/// <summary>Liest und schreibt Stufen — beides nur über den Ledger.</summary>
/// <remarks>
/// <para><strong>Synchron, je Aufruf, ohne jeden Zwischenspeicher</strong>
/// (ADR-0013). Ein Widerruf muss beim nächsten Lesen wirken, nicht beim
/// übernächsten. In einem Dienst, dessen ganzer Zweck gestufte Sichtbarkeit
/// ist, wäre ein Cache hier kein Leistungsdetail, sondern der Bruch der
/// Zusage.</para>
///
/// <para><strong>Geschrieben wird mit dem Token der Person.</strong> Der Ledger
/// verwaltet sich strikt selbst — er antwortet 403 „a consent belongs to its
/// subject", wenn jemand für einen anderen erteilt. Dass dieser Dienst im
/// Auftrag des Aufrufers fragt und schreibt, ist deshalb keine Bequemlichkeit,
/// sondern die Stelle, an der eine Stufenfreigabe unfälschbar wird: sie kann
/// nur gelingen, wenn die Person selbst sie auslöst.</para>
/// </remarks>
public interface IEinwilligungstor
{
    /// <summary>
    /// Welche Stufe gerade steht — <strong>eine Frage, alle Fähigkeiten</strong>.
    /// </summary>
    /// <remarks>
    /// Als Sammelfrage (ADR-0030): eine Frage je Fähigkeit wären fünf
    /// Verbindungsaufbauten für eine Zeile, und eine Liste von Gesprächen
    /// vervielfachte das. Die Antworten kommen in der Reihenfolge der Fragen,
    /// und eine abweichende Länge ist ein <em>Fehler</em> und kein Anlass zu
    /// raten.
    /// </remarks>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    Task<IReadOnlyList<Stufe>> StufenAsync(
        IReadOnlyList<(SubjectId Wer, TenantId Firma)> paare,
        CancellationToken cancellationToken = default);

    /// <summary>Erteilt die Fähigkeiten einer Stufe.</summary>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    /// <exception cref="EinwilligungAbgelehnt">Der Ledger sagt Nein.</exception>
    Task ErteileAsync(
        IReadOnlyList<string> faehigkeiten, CancellationToken cancellationToken = default);

    /// <summary>Nimmt Fähigkeiten zurück.</summary>
    /// <param name="grund">
    /// Der Ledger verlangt einen — ein Widerruf muss erklärbar sein. Was dieser
    /// Dienst schreibt, ist eine <em>Form</em> und nie ein Satz über einen
    /// Menschen.
    /// </param>
    /// <exception cref="EinwilligungSchweigt">Der Ledger antwortet nicht.</exception>
    /// <exception cref="EinwilligungAbgelehnt">Der Ledger sagt Nein.</exception>
    Task WiderrufeAsync(
        IReadOnlyList<string> faehigkeiten,
        string grund,
        CancellationToken cancellationToken = default);
}
