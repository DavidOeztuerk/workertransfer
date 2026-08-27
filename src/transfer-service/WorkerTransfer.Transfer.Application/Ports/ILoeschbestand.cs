using Girder.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>Was „löschen" in diesem Dienst heißt (ADR-0027 §2).</summary>
/// <remarks>
/// Fünf Anweisungen, und zwei laufen in verschiedene Richtungen:
/// <list type="bullet">
/// <item><c>market_status</c> fällt, samt Notiz.</item>
/// <item>Eine Anfrage <em>über</em> die Person fällt — die Zeile <em>ist</em>
/// eine Aussage über sie.</item>
/// <item>Eine Anfrage <em>von</em> der Person bleibt, ohne ihren Namen: sie
/// gehört dem Unternehmen und handelt von einem Dritten.</item>
/// <item>Ihre Vorgänge fallen, samt Nachricht und Angebotstext.</item>
/// <item>Offene Vermerke in der Outbox fallen.</item>
/// </list>
/// <para>
/// Der Schalter reist als Parameter herein statt drinnen gelesen zu werden.
/// Sonst ließe sich nur prüfen, dass er auf aus steht — nicht, was der
/// umgelegte Schalter abdeckt, und genau das ist die Aussage, auf die es
/// ankommt.
/// </para>
/// </remarks>
public interface ILoeschbestand
{
    /// <summary>Löscht alles über diesen Menschen.</summary>
    /// <param name="wer">Wessen.</param>
    /// <param name="bezahlteBehalten">
    /// Ob abgeschlossene Vorgänge mit Vergütung stehen bleiben. In der
    /// Voreinstellung <c>false</c> (<see cref="Loeschung.Aufbewahrung"/>).
    /// </param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Wie viele absichtlich stehen blieben. Voreingestellt: 0.</returns>
    Task<int> LoescheAsync(
        SubjectId wer,
        bool bezahlteBehalten,
        CancellationToken cancellationToken = default);
}
