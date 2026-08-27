using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>GitHub hat nicht geantwortet — ein Systemzustand, kein Ausgang.</summary>
/// <remarks>
/// Nicht auf „nicht bewiesen" abzubilden: das hieße, jemandem den Nachweis
/// abzusprechen, weil <em>wir</em> gerade nicht fragen konnten.
/// </remarks>
public sealed class GitHubSchweigt(string grund, Exception? ursache = null)
    : Exception(grund, ursache);

/// <summary>Der Zugriff auf GitHub — einmal lesen, nicht zusehen.</summary>
/// <remarks>
/// Es gibt hier bewusst <strong>keinen Hintergrundabgleich</strong>, keinen
/// Nachtlauf und keinen Webhook. ADR-0004 verbietet Scraping; der Buchstabe
/// wäre mit einem periodischen Abruf eingehalten, der Sinn nicht: eine
/// Plattform, die einem Menschen dauerhaft hinterhersieht, tut etwas anderes
/// als eine, die einmal auf seine Bitte hinsieht.
/// <para>
/// Nebenbei löst das die sechzig Anfragen pro Stunde: die Zahl der Abrufe hängt
/// an Handlungen von Menschen, nicht an einer Uhr.
/// </para>
/// </remarks>
public interface IGitHub
{
    /// <summary>Steht die Einmalzeichenfolge in der Beschreibung eines Gists?</summary>
    /// <exception cref="GitHubSchweigt">GitHub hat nicht geantwortet.</exception>
    Task<bool> HatNachweisgistAsync(
        string login, string einmalzeichenfolge, CancellationToken cancellationToken = default);

    /// <summary>Die öffentlichen Repositories dieses Kontos.</summary>
    /// <exception cref="GitHubSchweigt">GitHub hat nicht geantwortet.</exception>
    Task<IReadOnlyList<Repository>> RepositoriesAsync(
        string login, CancellationToken cancellationToken = default);
}
