using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Messages;
using WorkerTransfer.Scout.Application.Ports;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Application.Candidates;

/// <summary>A page of hits for a company.</summary>
/// <param name="Company">The caller's company — from the TOKEN, never from the request.</param>
/// <param name="Filter">What is searched for. Only what was named.</param>
/// <param name="PageLength">How many rows the page carries at most.</param>
/// <param name="Cursor">Where it continues, as text from the request.</param>
/// <param name="Job">
/// The advert the availability tick is formed against, or <c>null</c>.
/// </param>
/// <remarks>
/// <para><strong>The job does not filter.</strong> It changes nothing about the
/// set of hits — it adds a piece of information to each hit. Without it all of
/// them carry a dash, and that is a complete answer (ADR-0041).</para>
/// </remarks>
public sealed record CandidatesQuery(
    TenantId Company,
    SearchFilter Filter,
    int PageLength,
    string? Cursor,
    Guid? Job = null) : IQueryMessage<HitPage>;

/// <summary>
/// Searches over what was named, checks the ledger per row, fetches the evidence.
/// </summary>
/// <remarks>
/// <para><strong>The order of the four steps is this service's
/// promise</strong>, and each of them stands in ADR-0036:</para>
/// <list type="number">
/// <item>profile-service searches — <strong>exclusively over named
/// words</strong> (condition 3, ADR-0033), and as an OR: whoever names one of
/// the words is in. Under AND every tick would be set and the list
/// useless.</item>
/// <item>The ledger is asked <strong>once for the whole page</strong>,
/// synchronously and without a cache (ADR-0030, ADR-0013). A differing answer
/// length is an error, no occasion to guess.</item>
/// <item>What is not released drops out. <strong>Not reloaded</strong> until the
/// page is full (ADR-0020 §4), and <strong>no total</strong> (ADR-0026).</item>
/// <item>Only now, for the remaining hits, is evidence <strong>fetched
/// to</strong> them — never used for finding.</item>
/// </list>
///
/// <para><strong>No ordering by fit</strong> (condition 1). The order is the one
/// in which profile-service delivers: <c>updated_at DESC, id DESC</c> — stable
/// and unrelated to the matter. Nothing is rearranged here, and there is nothing
/// one could rearrange by either: no rank can be formed from a tick list without
/// inventing a number first. Whoever ever builds in an <c>OrderBy</c> computed
/// from the hit itself has opened ADR-0022 through the back door.</para>
/// </remarks>
public sealed class CandidatesHandler(
    IProfileSearch search,
    IConsentGate gate,
    IEvidenceSource evidenceSource,
    IJobLookup jobs) : IRequestHandler<CandidatesQuery, HitPage>
{
    /// <summary>The cap per page.</summary>
    /// <remarks>
    /// Every row costs a question to the ledger and an evidence fetch; without a
    /// cap a caller would build an arbitrarily expensive query with a single
    /// address line. And the ledger's batch question carries at most a hundred
    /// pairs — two per person, so fifty people.
    /// </remarks>
    public const int MaxPageLength = 50;

    /// <summary>What a page carries when nobody says otherwise.</summary>
    public const int DefaultPageLength = 20;

    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    /// <exception cref="ProfileSearchSilent">profile-service does not answer.</exception>
    public async Task<HitPage> Handle(
        CandidatesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var length = Math.Clamp(
            request.PageLength <= 0 ? DefaultPageLength : request.PageLength, 1, MaxPageLength);

        // 1. The search runs over what was NAMED. The filter carries nothing else
        //    — that stands as a type in `SearchFilter`.
        var page = await search.SearchAsync(
            request.Filter, length, request.Cursor, cancellationToken);

        if (page.Items.Count == 0)
        {
            return new HitPage([], page.Next);
        }

        // 2. ONE question for the whole page, synchronous and without a cache.
        var verdicts = await gate.MaySeeAllAsync(
            [.. page.Items.Select(profile => profile.Subject)], request.Company, cancellationToken);

        if (verdicts.Count != page.Items.Count)
        {
            // An answer that does not fit the questions cannot be assigned — and
            // assigning wrongly would mean showing the profile of the wrong
            // person. So refuse instead of guessing.
            throw new ConsentGateSilent(
                $"{verdicts.Count} answers to {page.Items.Count} questions");
        }

        // 3. The page may get shorter. Filling it up would reveal through the
        //    number of rounds how many profiles are NOT released.
        var visible = page.Items
            .Where((_, index) => verdicts[index])
            .ToArray();

        // 4a. The job, ONCE for the whole page — it is the same for all hits,
        //     and asking once per row would be the same mistake the batch
        //     question to the ledger fixed.
        //
        //     A failure is SWALLOWED: a job that cannot be fetched right now
        //     turns every tick into a dash and none into a cross. The search does
        //     not fail because of it — it loses one piece of information, and the
        //     dash says so.
        var jobStatement = await Statement(request.Job, cancellationToken);
        var atTheJob = jobStatement is null
            ? null
            : PlaceLookup.Find(jobStatement.PostalCode, jobStatement.Location);

        // 4b. Only now the evidence — and per hit, because each has a release of
        //     its own. One after the other and not in parallel: towards a foreign
        //     service a page with fifty simultaneous requests is a small attack,
        //     and the cap above keeps the number small enough.
        //
        //     Silent once means silent for this page. Each fetch has its own time
        //     limit, and asking a silent github-service again for every hit
        //     stacked them: fourteen hits took 30.9 s, measured 02.10.2026, and
        //     the company looked at an empty list all that time.
        var hits = new List<Hit>(visible.Length);
        var silent = false;

        foreach (var profile in visible)
        {
            var sheet = silent
                ? new EvidenceSheet([], EvidenceState.Unavailable)
                : await evidenceSource.GetAsync(profile.Subject, cancellationToken);

            silent = sheet.State == EvidenceState.Unavailable;

            hits.Add(new Hit(
                profile.Subject,
                profile.Headline,
                profile.Bio,
                profile.Location,
                profile.RemoteOk,
                profile.Named,
                Ticks(request.Filter, profile.Named),
                sheet.Evidence,
                sheet.State,
                Availability.Form(
                    profile.Commute,
                    jobStatement?.Attendance,
                    // The person's place is resolved PER HIT and stored nowhere:
                    // no coordinate column on a human being arises (ADR-0041 §4).
                    PlaceLookup.Find(profile.Location),
                    atTheJob)));
        }

        return new HitPage(hits, page.Next);
    }

    /// <summary>The advert's statement — or nothing if it is silent.</summary>
    /// <remarks>
    /// The failure is swallowed here and not passed on: without an advert there
    /// is no reason to lose the whole page of hits. The price is a dash instead of
    /// a tick, and the dash says exactly the right thing — "nothing is said about
    /// this".
    /// </remarks>
    private async Task<JobStatement?> Statement(
        Guid? job, CancellationToken cancellationToken)
    {
        if (job is not { } which)
        {
            return null;
        }

        try
        {
            return await jobs.GetAsync(which, cancellationToken);
        }
        catch (JobLookupSilent)
        {
            return null;
        }
    }

    /// <summary>The tick list: a yes or no per searched word.</summary>
    /// <remarks>
    /// <para>Per <em>searched</em> word and not per named skill: the list answers
    /// the company's question and does not summarise the human being. Whoever
    /// searched for nothing gets an empty list — and expressly no "0 of 0".</para>
    ///
    /// <para>Compared canonically and ignoring case: both sides ran through the
    /// same vocabulary (ADR-0023), and a tick that fails on the spelling would be
    /// a false statement about a human being.</para>
    ///
    /// <para>The order is the search's. Ordering the ticks "fulfilled first" would
    /// be an ordering by fit in the small — and the beginning of one in the
    /// large.</para>
    /// </remarks>
    private static IReadOnlyList<Tick> Ticks(
        SearchFilter filter, IReadOnlyList<string> named) =>
    [
        .. filter.NamedWords.Select(word => new Tick(
            word,
            named.Contains(word, StringComparer.OrdinalIgnoreCase)))
    ];
}
