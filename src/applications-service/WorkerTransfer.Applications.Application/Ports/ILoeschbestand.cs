using Girder.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Was „löschen" in diesem Dienst heißt (ADR-0027 §2).</summary>
/// <remarks>
/// Der Schalter reist als Parameter herein statt drinnen gelesen zu werden.
/// Das ist der Unterschied zwischen „der Schalter steht auf aus" und „der
/// umgelegte Schalter deckt genau eine Zeilenklasse ab" — die zweite Aussage
/// ist die, die jemand prüfen können muss, ohne dafür etwas umzustellen.
/// </remarks>
public interface ILoeschbestand
{
    /// <summary>Löscht die Bewerbungen dieses Menschen.</summary>
    /// <param name="wer">Wessen.</param>
    /// <param name="eingestellteBehalten">
    /// Ob <c>hired</c>-Zeilen stehen bleiben. In der Voreinstellung
    /// <c>false</c> (<see cref="Loeschung.Aufbewahrung"/>).
    /// </param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Wie viele absichtlich stehen blieben. Voreingestellt: 0.</returns>
    Task<int> LoescheAsync(
        SubjectId wer,
        bool eingestellteBehalten,
        CancellationToken cancellationToken = default);
}
