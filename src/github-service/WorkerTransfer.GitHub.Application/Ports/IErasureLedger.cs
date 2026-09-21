using Noelia.Core.Identity;

namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>What "erase" means in this service (ADR-0027 §2).</summary>
/// <remarks>
/// One statement, without exception: the row falls completely — login, one-time
/// string and the snapshot.
/// <para>
/// The login is public; anybody can look it up on github.com. The personal
/// datum is not the name but the <strong>link</strong>: "this platform person
/// is that GitHub name". It comes into being here, and here it falls.
/// </para>
/// <para>
/// <strong>No retention switch.</strong> There is nothing here that belongs to
/// a company.
/// </para>
/// </remarks>
public interface IErasureLedger
{
    /// <summary>Erases the connection.</summary>
    /// <returns>What stayed. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
