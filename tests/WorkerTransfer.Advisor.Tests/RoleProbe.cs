using WorkerTransfer.ServiceDefaults.Roles;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>identity-service's role lookup, answered by the test.</summary>
/// <remarks>
/// Defaults to <c>recruiter</c>: the least role a first contact needs
/// (ADR-0048) — so every other test here measures what it measured before,
/// and the role tests change only this one value.
/// </remarks>
public sealed class RoleProbe : ICompanyRoles
{
    /// <summary>What it answers.</summary>
    public CompanyRole Answer { get; set; } = CompanyRole.Recruiter;

    /// <summary>Whether it stays silent instead — identity-service being out.</summary>
    public bool Silent { get; set; }

    /// <summary>What it was last asked — the company must come from the token.</summary>
    public (Guid Subject, Guid Company)? Last { get; private set; }

    /// <inheritdoc />
    public Task<CompanyRole> RoleAsync(
        Guid subject, Guid company, CancellationToken cancellationToken = default)
    {
        Last = (subject, company);

        return Silent
            ? throw new RoleLookupSilent("The probe is silent.")
            : Task.FromResult(Answer);
    }
}
