using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>Asks for the members of a company — for notifications.</summary>
/// <remarks>
/// Used when an application arrives or a draft is sent. The members are fetched
/// over identity-service's internal wire, with the same shared secret as the
/// notification path.
/// <para>
/// The port carries ids, not the wire contract: the application layer knows no
/// serialisation. What travels over HTTP is mapped by the infrastructure.
/// </para>
/// </remarks>
public interface ICompanyMemberLookup
{
    /// <summary>All members of a company.</summary>
    Task<IReadOnlyList<SubjectId>> GetAsync(
        TenantId company,
        CancellationToken cancellationToken = default);
}
