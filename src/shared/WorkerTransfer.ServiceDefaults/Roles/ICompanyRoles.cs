namespace WorkerTransfer.ServiceDefaults.Roles;

/// <summary>Who holds which role in which company.</summary>
/// <remarks>
/// <para>A port, not a service: today the answer comes over the internal wire
/// from identity-service, and in a test suite out of a fake. Without it every
/// suite that touches a company endpoint would have to start a second service
/// alongside.</para>
/// </remarks>
public interface ICompanyRoles
{
    /// <summary>The role, or <see cref="CompanyRole.None"/>.</summary>
    /// <param name="subject">Who is asking.</param>
    /// <param name="company">For which company.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <exception cref="RoleLookupSilent">
    /// The lookup could not be had. <strong>Not the same as
    /// <see cref="CompanyRole.None"/>:</strong> answering "we do not know" with
    /// "you may not" would be a lie, and one that looks like a decision.
    /// </exception>
    Task<CompanyRole> RoleAsync(
        Guid subject,
        Guid company,
        CancellationToken cancellationToken = default);
}

/// <summary>The role lookup did not answer.</summary>
/// <remarks>
/// Leads to 503 and never to 403 — the same distinction this tree already makes
/// at the consent ledger: 503 means "did not answer", not "no".
/// </remarks>
public sealed class RoleLookupSilent : Exception
{
    /// <summary>With a reason that names no values.</summary>
    /// <param name="message">What was missing — a shape, never a value.</param>
    public RoleLookupSilent(string message) : base(message)
    {
    }

    /// <summary>With a reason and the cause underneath it.</summary>
    /// <param name="message">What was missing.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public RoleLookupSilent(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Parameterless, because the analyser demands it.</summary>
    public RoleLookupSilent()
    {
    }
}
