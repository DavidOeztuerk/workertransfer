using Noelia.Core.Identity;

namespace WorkerTransfer.Advisor.Application.Ports;

/// <summary>Another service did not answer.</summary>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class LookupSilent(string reason) : Exception(reason);

/// <summary>Who a company is — name and proven domain.</summary>
/// <param name="Domain">
/// The domain the company was claimed on (ADR-0019). It is the only interesting
/// part here: it decides whether a person has excluded this company.
/// </param>
public sealed record CompanyCard(string Name, string Domain);

/// <summary>Asks identity-service who a company is.</summary>
public interface ICompanyLookup
{
    /// <summary>The company, or <c>null</c> if there is none.</summary>
    /// <exception cref="LookupSilent">identity-service does not answer.</exception>
    Task<CompanyCard?> GetAsync(TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>A human being's civil name and contact.</summary>
/// <remarks>
/// Only these two. No postal address, no phone: ADR-0038 files the postal
/// address as a <em>template</em> for the letterhead, and it belongs to an
/// application the person sends themselves — not into the view of a company
/// holding a conversation.
/// </remarks>
public sealed record PersonCard(string Name, string Email);

/// <summary>Asks identity-service what a human being is called.</summary>
/// <remarks>
/// <strong>It is asked only after the ledger has confirmed stage 3.</strong>
/// The order is the promise: nothing is looked up about somebody who released
/// nothing — and it would otherwise stand in the other service's log.
/// </remarks>
public interface IPersonLookup
{
    /// <summary>The human being, or <c>null</c>.</summary>
    /// <exception cref="LookupSilent">identity-service does not answer.</exception>
    Task<PersonCard?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);
}

/// <summary>The handover did not come about.</summary>
/// <remarks>
/// transfer-service answered, and it refused — for instance because the market
/// status is not released or the person is not approachable. Those conditions
/// live <em>there</em> and are not repeated here; what happens here is passing
/// the refusal on honestly.
/// </remarks>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class HandoverRefused(string reason) : Exception(reason);

/// <summary>Turns an agreement into a case in transfer-service.</summary>
/// <remarks>
/// <para><strong>The triangle consensus is not rebuilt.</strong>
/// transfer-service itself checks on creation whether the market status is
/// released to this company and whether the person is approachable; it also
/// records whether the current employer's release is needed. This port calls
/// exactly that door — it knows none of those rules.</para>
/// </remarks>
public interface ICaseHandover
{
    /// <summary>Creates the case and returns its id.</summary>
    /// <exception cref="LookupSilent">transfer-service does not answer.</exception>
    /// <exception cref="HandoverRefused">transfer-service refuses.</exception>
    Task<Guid> HandOverAsync(
        SubjectId subject, string occasion, CancellationToken cancellationToken = default);
}
