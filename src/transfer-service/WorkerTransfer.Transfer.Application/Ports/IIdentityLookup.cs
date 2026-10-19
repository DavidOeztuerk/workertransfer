using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>identity-service did not answer. No result — a system state, 503.</summary>
public sealed class IdentitySilent(string reason, Exception? cause = null) : Exception(reason, cause);

/// <summary>identity-service's internal doors, behind the shared secret (ADR-0050).</summary>
/// <remarks>
/// <para><strong>The secret proves that a service asks, not that it may.</strong>
/// The person's name goes to her employer only after the ledger has said that
/// the route to that employer is open; the handler asks the ledger first, and
/// whoever calls this for something else fetches that check along with it.</para>
/// </remarks>
public interface IIdentityLookup
{
    /// <summary>Whether this company exists.</summary>
    /// <exception cref="IdentitySilent">identity-service did not answer.</exception>
    Task<bool> CompanyExistsAsync(TenantId company, CancellationToken cancellationToken = default);

    /// <summary>The person's civil name, or <c>null</c>: no such account.</summary>
    /// <exception cref="IdentitySilent">identity-service did not answer.</exception>
    Task<string?> PersonNameAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>The members of a company — a company has no mailbox, its members do.</summary>
    /// <exception cref="IdentitySilent">identity-service did not answer.</exception>
    Task<IReadOnlyList<SubjectId>> MembersAsync(TenantId company, CancellationToken cancellationToken = default);
}
