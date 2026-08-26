using Girder.Core.Identity;

namespace WorkerTransfer.Notification.Application.Ports;

/// <summary>Was „löschen" in diesem Dienst heißt (ADR-0027 §2).</summary>
/// <remarks>
/// Zwei Anweisungen, beide ohne Ausnahme: das Postfach und die Wünsche fallen.
/// <strong>Kein Aufbewahrungsschalter</strong> — es wurde nie behauptet, dass
/// eine Zeile hier einer Aufbewahrungspflicht unterläge, und einer „für alle
/// Fälle" ist genau die Vorsichtsannahme, die ADR-0027 §3 abschafft.
/// </remarks>
public interface ILoeschbestand
{
    /// <summary>Löscht alles über diesen Menschen.</summary>
    /// <returns>Was stehen blieb. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
