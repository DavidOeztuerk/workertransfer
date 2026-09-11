using WorkerTransfer.Scout.Domain.Treffer;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>Was eine Stellenanzeige über ihre Anwesenheit und ihren Ort sagt.</summary>
/// <param name="Ort">Der Freitext-Ort der Anzeige.</param>
/// <param name="Postleitzahl">Sie zuerst, weil sie eindeutig ist (ADR-0032).</param>
/// <param name="Anwesenheit">remote / hybrid / vor Ort, oder <c>null</c>.</param>
/// <remarks>
/// <strong>Drei Felder, und mehr braucht es nicht.</strong> Kein Titel, keine
/// Beschreibung, keine Fähigkeiten: was dieser Dienst von einer Anzeige
/// wissen muss, um ein Häkchen zu bilden, ist wo sie liegt und ob jemand
/// hinkommen muss. Alles Weitere wäre eine Kopie fremder Daten (ADR-0004).
/// </remarks>
public sealed record Stellenaussage(string Ort, string Postleitzahl, Anwesenheit? Anwesenheit);

/// <summary>jobs-service hat nicht geantwortet.</summary>
/// <remarks>
/// Ein eigener Fehler — aber einer, den der Suchhandler <em>schluckt</em>: eine
/// Stelle, die gerade nicht abrufbar ist, macht aus jedem Treffer einen Strich
/// und aus keinem ein Kreuz. Die Suche selbst fällt deswegen nicht aus; sie
/// verliert nur eine Auskunft (ADR-0041).
/// </remarks>
/// <param name="grund">Die Art des Fehlschlags.</param>
public sealed class StelleSchweigt(string grund) : Exception(grund);

/// <summary>Holt die Aussage einer Anzeige — nie, um jemanden zu finden.</summary>
/// <remarks>
/// <para>Wie die Belege: sie wird zum Treffer <em>dazugeholt</em> und ist am
/// Finden unbeteiligt. Die Suche läuft über genannte Fähigkeiten, Ort und
/// Remote; die Stelle kommt erst danach ins Spiel und ändert an der Menge der
/// Treffer <strong>nichts</strong> — kein Wegfiltern (ADR-0041).</para>
///
/// <para>Gefragt wird die öffentliche Route <c>GET /jobs/{id}</c>: eine
/// veröffentlichte Anzeige ist eine Aussage des Unternehmens an alle, und sie
/// braucht keine eigene Tür.</para>
/// </remarks>
public interface IStellen
{
    /// <summary>Die Aussage einer Anzeige, oder <c>null</c>, wenn es sie nicht gibt.</summary>
    /// <exception cref="StelleSchweigt">jobs-service antwortet nicht.</exception>
    Task<Stellenaussage?> HoleAsync(Guid stelle, CancellationToken cancellationToken = default);
}
