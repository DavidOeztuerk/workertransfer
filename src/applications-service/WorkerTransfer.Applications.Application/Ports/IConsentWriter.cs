using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>The ledger did not answer. Not a result — a system state.</summary>
/// <remarks>
/// Deliberately not a rule violation: an application without its release would
/// be one the company cannot read — and the other way round would be worse. The
/// endpoint turns it into 503, which is neither "sent" nor "rejected".
/// </remarks>
public sealed class ConsentGateSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>The release keys, built in exactly one place.</summary>
public static class ConsentKey
{
    /// <summary>
    /// Which releases an application grants.
    /// </summary>
    /// <remarks>
    /// The profile always goes along — an application without any statement
    /// about the person is none. Résumé and portfolio only when the person sends
    /// them along. Each one names <em>this one</em> recipient, never "all":
    /// whoever applies to one company has told the others nothing.
    /// </remarks>
    public static IReadOnlyList<string> For(
        TenantId company, bool resume, bool portfolio, bool documents = false)
    {
        var keys = new List<string> { $"profile.visibility:tenant:{company}" };

        if (resume)
        {
            keys.Add($"resume.visibility:tenant:{company}");
        }

        if (portfolio)
        {
            keys.Add($"portfolio.visibility:tenant:{company}");
        }

        if (documents)
        {
            // A capability of ITS OWN and not carried along under `resume`:
            // references and certificates are a different class of data than
            // the career history, and whoever wants to withdraw them singly
            // must be able to. A capability that releases two things can only be
            // revoked wholly or not at all.
            keys.Add($"documents.visibility:tenant:{company}");
        }

        return keys;
    }

    /// <summary>Everything an application can ever have granted.</summary>
    /// <remarks>
    /// The withdrawal revokes all of them <em>unconditionally</em>, including
    /// those that were perhaps never granted. That closes the gap a successful
    /// ledger call with a failed commit would have left, and the ledger
    /// tolerates a revocation without a prior grant.
    /// </remarks>
    public static IReadOnlyList<string> All(TenantId company) =>
        For(company, resume: true, portfolio: true, documents: true);
}

/// <summary>Writes to the ledger — and never reads from it.</summary>
/// <remarks>
/// That it only writes here is no gap: an application holds no profile data but
/// names a <c>subject_id</c>. Whoever wants to see profile, résumé or portfolio
/// asks the responsible services, and there the ledger applies. A read check
/// here would be a second place answering the same question — and one day
/// differently.
/// </remarks>
public interface IConsentWriter
{
    /// <summary>Grants the releases this application brings along.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task GrantAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default);

    /// <summary>Takes them back.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task RevokeAsync(
        SubjectId subject,
        IReadOnlyList<string> capabilities,
        CancellationToken cancellationToken = default);
}
