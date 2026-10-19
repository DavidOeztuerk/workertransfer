using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Application.Ports;

/// <summary>What an application needs from the job.</summary>
/// <remarks>
/// What stands here also stands in the ADVERT — every field can be read
/// publicly, without signing in. That is the condition for it being allowed
/// into a cover-letter context (ADR-0034): the letter refers to what the company
/// wrote itself, and to nothing else.
/// <para>
/// <c>Company</c> is the exception and not a statement: it says whom the job
/// belongs to, so the application finds its way there. It does not go into the
/// prompt.
/// </para>
/// </remarks>
public sealed record PublicJob(
    Guid Id,
    TenantId Company,
    string Title,
    string Description = "",
    string Location = "",
    IReadOnlyList<string>? Skills = null)
{
    /// <summary>The skills the advert names.</summary>
    public IReadOnlyList<string> Skills { get; init; } = Skills ?? [];
}

/// <summary>jobs-service is silent — we do not know whether the job exists.</summary>
/// <remarks>
/// Not to be treated as "does not exist": that would be a statement about an
/// advert that may well be open, and the person would get a rejection nobody
/// gave.
/// </remarks>
public sealed class JobLookupSilent(string reason, Exception? cause = null)
    : Exception(reason, cause);

/// <summary>Asks jobs-service for a public job.</summary>
public interface IJobLookup
{
    /// <summary>The job, or <c>null</c> when it does not exist publicly.</summary>
    /// <remarks>
    /// <c>null</c> covers "does not exist", "is still a draft" and "is closed" —
    /// jobs-service keeps the three indistinguishable anyway, and that is exactly
    /// right here.
    /// </remarks>
    /// <exception cref="JobLookupSilent">jobs-service did not answer.</exception>
    Task<PublicJob?> GetAsync(Guid job, CancellationToken cancellationToken = default);
}
