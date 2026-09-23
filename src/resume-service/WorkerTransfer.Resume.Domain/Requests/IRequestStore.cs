using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Requests;

/// <summary>Where the requests are kept.</summary>
public interface IRequestStore
{
    /// <summary>One by its id, or <c>null</c>.</summary>
    Task<ResumeRequest?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The one this company already made about this person, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// The pair is unique in the database as well, and that is the rule "ask
    /// once": a refusal is worthless if the same company may ask again — whoever
    /// may ask three times did not get a no, they got a delay. In the handler
    /// alone two simultaneous requests would both pass the check.
    /// </remarks>
    Task<ResumeRequest?> FindAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Adds a new one to the open transaction.</summary>
    Task AppendAsync(ResumeRequest requestRecord, CancellationToken cancellationToken = default);

    /// <summary>Writes an answered one back.</summary>
    Task SaveAsync(ResumeRequest requestRecord, CancellationToken cancellationToken = default);

    /// <summary>Everything asked about this person — the transparency list.</summary>
    Task<IReadOnlyList<ResumeRequest>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Everything this company asked.</summary>
    Task<IReadOnlyList<ResumeRequest>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);
}
