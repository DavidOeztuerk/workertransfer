using Girder.Core.Identity;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>Was „löschen" in diesem Dienst heißt (ADR-0027 §2).</summary>
/// <remarks>
/// Eine Anweisung, ohne Ausnahme: die Zeile fällt vollständig — Login,
/// Einmalzeichenfolge und der Abzug.
/// <para>
/// Der Login ist öffentlich; jeder kann ihn auf github.com nachschlagen. Das
/// Personendatum ist nicht der Name, sondern die <strong>Verknüpfung</strong>:
/// „dieser Plattform-Mensch ist jener GitHub-Name". Sie entsteht hier, und hier
/// fällt sie.
/// </para>
/// <para>
/// <strong>Kein Aufbewahrungsschalter.</strong> Es gibt hier nichts, was einem
/// Unternehmen gehörte.
/// </para>
/// </remarks>
public interface ILoeschbestand
{
    /// <summary>Löscht die Verbindung.</summary>
    /// <returns>Was stehen blieb. Immer null.</returns>
    Task<int> LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default);
}
