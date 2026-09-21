using Noelia.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Ports;

/// <summary>The application address — a template, never the search, never AI.</summary>
public interface IPostalAddresses
{
    /// <summary>The template, or empty when nothing was ever set.</summary>
    Task<PostalAddress> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Writes it back.</summary>
    Task SaveAsync(PostalAddress address, CancellationToken cancellationToken = default);
}
