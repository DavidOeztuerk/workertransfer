using WorkerTransfer.Scout.Domain.Hits;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>What a job advert says about its attendance and its place.</summary>
/// <param name="Location">The advert's free-text location.</param>
/// <param name="PostalCode">That first, because it is unambiguous (ADR-0032).</param>
/// <param name="Attendance">remote / hybrid / on site, or <c>null</c>.</param>
/// <remarks>
/// <strong>Three fields, and no more is needed.</strong> No title, no
/// description, no skills: what this service has to know about an advert to
/// form a tick is where it lies and whether somebody has to come. Anything more
/// would be a copy of foreign data (ADR-0004).
/// </remarks>
public sealed record JobStatement(
    string Location,
    string PostalCode,
    Attendance? Attendance,
    Guid Company = default,
    IReadOnlyList<string>? Skills = null,
    bool Published = false);

/// <summary>jobs-service did not answer.</summary>
/// <remarks>
/// An error of its own — but one the search handler <em>swallows</em>: a job
/// that cannot be fetched right now turns every hit into a dash and none into a
/// cross. The search itself does not fail because of it; it only loses one piece
/// of information (ADR-0041).
/// </remarks>
/// <param name="reason">The kind of the failure.</param>
public sealed class JobLookupSilent(string reason) : Exception(reason);

/// <summary>Fetches an advert's statement — never to find anybody.</summary>
/// <remarks>
/// <para>Like the evidence: it is <em>fetched to</em> the hit and takes no part
/// in finding. The search runs over named skills, location and remote; the job
/// comes into play only afterwards and changes <strong>nothing</strong> about
/// the set of hits — no filtering out (ADR-0041).</para>
///
/// <para>What is asked is the public route <c>GET /jobs/{id}</c>: a published
/// advert is a statement of the company to everybody, and it needs no door of
/// its own.</para>
/// </remarks>
public interface IJobLookup
{
    /// <summary>An advert's statement, or <c>null</c> if it does not exist.</summary>
    /// <exception cref="JobLookupSilent">jobs-service does not answer.</exception>
    Task<JobStatement?> GetAsync(Guid job, CancellationToken cancellationToken = default);
}
