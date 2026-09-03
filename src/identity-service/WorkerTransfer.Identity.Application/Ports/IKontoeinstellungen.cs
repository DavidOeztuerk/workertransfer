using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Liest und schreibt, was eine Person über sich entschieden hat.</summary>
public interface IKontoeinstellungen
{
    /// <summary>Die Einstellungen — oder die Vorgabe, wenn nie etwas gesetzt wurde.</summary>
    /// <param name="wer">Wessen.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    /// <returns>Nie <c>null</c>: „nichts gesetzt" ist ein Zustand, kein Fehlen.</returns>
    Task<Kontoeinstellungen> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Schreibt sie zurück.</summary>
    /// <param name="einstellungen">Was gilt.</param>
    /// <param name="cancellationToken">Abbruch.</param>
    Task SichereAsync(
        Kontoeinstellungen einstellungen, CancellationToken cancellationToken = default);
}
