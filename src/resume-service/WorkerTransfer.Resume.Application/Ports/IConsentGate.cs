using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Application.Ports;

/// <summary>The ledger did not answer. Not a business outcome — a system state.</summary>
/// <remarks>
/// Deliberately not a rule violation: <c>false</c> would be a statement about
/// the person that nobody can make right now. The endpoint turns it into 503,
/// which is neither "there is nothing here" nor showing the résumé.
/// </remarks>
public sealed class ConsentGateSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>The capability strings, built in exactly one place.</summary>
/// <remarks>
/// If the interface built them there would be two places that had to agree on
/// the format, and one of them would be in a browser.
/// </remarks>
public static class ConsentKey
{
    /// <summary>
    /// Whoever may not see the profile may not ask for the résumé either.
    /// </summary>
    /// <remarks>
    /// Otherwise the request would be a channel for learning that a person
    /// exists here at all.
    /// </remarks>
    public const string Profile = "profile.visibility:public";

    /// <summary>
    /// The release names its recipient — this one company, never "everybody".
    /// </summary>
    /// <remarks>
    /// <c>profile.visibility:public</c> says "all companies";
    /// <c>resume.visibility:tenant:&lt;id&gt;</c> says "this one". There is no
    /// public counterpart and there must not be one: a résumé names real
    /// employers with dates, which is exactly what a current employer must not
    /// see.
    /// </remarks>
    public static string CurriculumVitae(TenantId company) => $"resume.visibility:tenant:{company}";

    /// <summary>References and certificates — a capability of their own.</summary>
    /// <remarks>
    /// <strong>Not carried along under <c>resume</c></strong>, although both are
    /// called "documents": the career history is self-written text, a reference
    /// is a third party's document with names, grades and signatures. Whoever
    /// wants to show the one and not the other must be able to — and a
    /// capability that releases two things can only be withdrawn wholly or not
    /// at all (ADR-0035).
    /// <para>
    /// Here too without a public counterpart: there is
    /// <c>documents.visibility:tenant:&lt;id&gt;</c> and nothing that would mean
    /// "everybody".
    /// </para>
    /// </remarks>
    public static string Documents(TenantId company) => $"documents.visibility:tenant:{company}";
}

/// <summary>Reads <em>and</em> writes the ledger — unlike the profile service.</summary>
/// <remarks>
/// The person grants and withdraws through this service so that the capability
/// string is built in one place. Read synchronously and cached nowhere
/// (ADR-0013): a withdrawal has to take effect on the very next read, not on
/// the one after.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Whether this person's profile is released at all.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MaySeeProfileAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Whether this company may read this person's résumé right now.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MayReadResumeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Whether this company may see this person's documents.</summary>
    /// <remarks>
    /// Synchronous and without a cache like everything here: a revocation must
    /// take effect on the next call, not on the one after (ADR-0013).
    /// </remarks>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MaySeeDocumentsAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same question for several pairs, in one round trip.
    /// </summary>
    /// <remarks>
    /// The answers come back <em>in the order of the questions</em>, one per
    /// pair. Nothing is cached and nothing is held in reserve — a batch is one
    /// question in one request, not a stock answer.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">
    /// The ledger did not answer, or answered a different number of times than
    /// it was asked.
    /// </exception>
    Task<IReadOnlyList<bool>> MayEachReadResumeAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>Records the release for this one company.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task GrantAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Takes it back.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
