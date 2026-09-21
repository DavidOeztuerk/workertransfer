using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>Die Bewerbungsanschrift — ResumeTemplate, nie Search, nie KI.</summary>
public interface IPostalAddresses
{
    /// <summary>Die ResumeTemplate, oder leer, wenn nie etwas gesetzt wurde.</summary>
    Task<PostalAddress> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Schreibt sie zurück.</summary>
    Task SaveAsync(PostalAddress anschrift, CancellationToken cancellationToken = default);
}
