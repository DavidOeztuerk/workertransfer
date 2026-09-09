using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Die Bewerbungsanschrift — Vorlage, nie Suche, nie KI.</summary>
public interface IAnschriften
{
    /// <summary>Die Vorlage, oder leer, wenn nie etwas gesetzt wurde.</summary>
    Task<Anschrift> HoleAsync(SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>Schreibt sie zurück.</summary>
    Task SichereAsync(Anschrift anschrift, CancellationToken cancellationToken = default);
}
