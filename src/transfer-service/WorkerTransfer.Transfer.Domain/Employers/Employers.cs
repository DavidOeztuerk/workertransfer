using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Employers;

/// <summary>Whether a company lets other companies ask about its staff (C1, ADR-0050).</summary>
/// <remarks>
/// <para><strong>Never public and never listed.</strong> "X lets its staff be
/// asked about", read from outside, is the rumour this setting must not start
/// — which is also why no word like "insolvency" stands anywhere near it.</para>
///
/// <para>It decides whether a <em>new</em> inquiry may be made. A running one
/// the company can always refuse, setting or not.</para>
/// </remarks>
public sealed record EmployerSetting(TenantId Company, bool AcceptsInquiries, DateTimeOffset ChangedAt);

/// <summary>The person's own "my employer is X — and X may be asked" (ADR-0050).</summary>
/// <remarks>
/// <para><strong>Naming and consenting are one act.</strong> Without the
/// consent this row does not exist, so "works at X" never comes into being
/// for somebody who did not want the route — the record the transfer draft of
/// 02.08.2026 refused to create.</para>
///
/// <para><strong>The consent itself stands in the ledger</strong>
/// (<c>employer.route:tenant:&lt;X&gt;</c>), asked at every read. This row is
/// only the pointer the ledger cannot give, because the ledger is not
/// searchable by prefix. A revocation on <c>/consents</c> closes the route
/// while the row still stands.</para>
/// </remarks>
public sealed record EmployerRoute(SubjectId Subject, TenantId Employer, DateTimeOffset OpenedAt);

/// <summary>Where the companies' settings live.</summary>
public interface IEmployerSettingStore
{
    /// <summary>This company's setting, or <c>null</c>: never set, which means "no".</summary>
    Task<EmployerSetting?> GetAsync(TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(EmployerSetting setting, CancellationToken cancellationToken = default);
}

/// <summary>Where the routes live — one per person.</summary>
public interface IEmployerRouteStore
{
    /// <summary>This person's route, or <c>null</c>.</summary>
    Task<EmployerRoute?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces.</summary>
    Task SaveAsync(EmployerRoute route, CancellationToken cancellationToken = default);

    /// <summary>Removes it. Nothing of it remains.</summary>
    Task DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
