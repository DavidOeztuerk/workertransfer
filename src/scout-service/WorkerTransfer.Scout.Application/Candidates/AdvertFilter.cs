using MediatR;
using Noelia.Core.Identity;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Candidates;

/// <summary>"Who fits this advert?" — the filter, taken from the advert itself (VISION-FRAGEN B1).</summary>
/// <remarks>
/// <para><strong>The scout per advert is the existing search with the advert's
/// words.</strong> No second mechanism: the result goes through
/// <see cref="CandidatesQuery"/> with all four conditions of ADR-0036 — ticks,
/// no ordering by fit, the ledger per page, no total. Fixed rules, no model:
/// that keeps it outside Art. 3 no. 1 of the AI Act (VISION-FRAGEN D3).</para>
///
/// <para><strong>Activated by the company, per view.</strong> There is no
/// nightly run and no stored list of people per advert; every opening asks
/// the ledger again (ADR-0013). A stored result would be a cache of people
/// with an alarm clock in front of it.</para>
///
/// <para><strong>Only the company's own, published advert.</strong> A draft
/// asks for nobody yet, a closed one for nobody any more, and a foreign one is
/// none of the caller's business — all three are the same 404.</para>
/// </remarks>
public sealed record AdvertFilterQuery(TenantId Company, Guid Job) : IQueryMessage<SearchFilter>;

/// <summary>No advert of this company to search for — 404, whatever the reason.</summary>
public sealed class NoAdvert() : Exception("No such advert");

/// <summary>Reads the advert and builds the filter from its skills.</summary>
public sealed class AdvertFilterHandler(IJobLookup jobs) : IRequestHandler<AdvertFilterQuery, SearchFilter>
{
    /// <inheritdoc />
    /// <exception cref="NoAdvert">Foreign, draft, closed or missing.</exception>
    /// <exception cref="JobLookupSilent">jobs-service does not answer — here NOT swallowed: without the advert there is no question.</exception>
    public async Task<SearchFilter> Handle(AdvertFilterQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var advert = await jobs.GetAsync(request.Job, cancellationToken);

        if (advert is null || advert.Company != request.Company.Value || !advert.Published)
        {
            throw new NoAdvert();
        }

        return SearchFilter.ForAdvert(advert.Skills ?? []);
    }
}
