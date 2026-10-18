using Noelia.Core.Identity;

namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>A page of jobs and how many there are in total.</summary>
/// <remarks>
/// <strong>A total instead of a cursor</strong>, since the list can be paged:
/// whoever wants to see "page 3 of 9" and jump there needs the number. A cursor
/// can only go forwards, and it never answers the question "how many are there
/// at all".
/// <para>
/// What is counted are ADVERTS. On a candidate list the same number would be a
/// statement about people and thereby contrary to ADR-0026; that is why it
/// stands here and not in a shared list type.
/// </para>
/// </remarks>
/// <param name="Items">The jobs of this page.</param>
/// <param name="Total">How many jobs the filter hits in total.</param>
/// <param name="Unlocated">
/// How many adverts a radius search could NOT judge because their location is
/// unknown. Always zero without a radius search.
/// <para>
/// The interface has to name this number. Leaving it out silently would be the
/// same lie by omission that ADR-0022 §3 forbids: whoever searches for "25 km
/// around me" would otherwise take the result for complete, although the filter
/// could say nothing at all about part of the adverts.
/// </para>
/// </param>
public sealed record JobPage(IReadOnlyList<Job> Items, int Total, int Unlocated = 0);

/// <summary>Finds and stores adverts.</summary>
public interface IJobStore
{
    /// <summary>An advert, or <c>null</c>.</summary>
    Task<Job?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Job job, CancellationToken cancellationToken = default);

    /// <summary>A company's adverts, in every state.</summary>
    Task<IReadOnlyList<Job>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>
    /// The public list — published ones only.
    /// </summary>
    /// <remarks>
    /// The state is filtered here and not by the caller: a parameter
    /// <c>status=draft</c> would be a way to read other companies' drafts.
    /// </remarks>
    /// <param name="page">
    /// The page, 1-based. People count from one, and this number stands like
    /// that in the address bar.
    /// </param>
    /// <param name="pageLength">How many adverts one page holds.</param>
    /// <param name="skills">What is asked for; <c>null</c> or empty means "anything".</param>
    /// <param name="location">
    /// A text filter over the location. Empty means "anywhere".
    /// </param>
    /// <param name="remote">How much remote; <c>null</c> means "any".</param>
    /// <param name="searchTerm">
    /// Free text over title and description. Empty means "everything".
    /// </param>
    /// <param name="company">
    /// Only one company's adverts — for the careers page. <c>null</c> means
    /// "all". Without this filter <c>/careers/&lt;slug&gt;</c> showed the adverts
    /// of ALL companies under the name of a single one; the interface had always
    /// sent the parameter and the service had never read it.
    /// </param>
    /// <param name="employment">
    /// Full-time, part-time and so on. Empty means "everything".
    /// </param>
    /// <param name="radius">
    /// "At most N kilometres from here." <c>null</c> means "wherever".
    /// <para>
    /// Jobs advertised as fully remote are ALWAYS included, whatever the
    /// radius: from where they can be reached is no question of distance for
    /// them. Hiding them because of a location filter would mean hiding exactly
    /// the adverts that are the most interesting for somebody outside the
    /// conurbations.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<JobPage> SearchAsync(
        int page,
        int pageLength,
        IReadOnlyList<string>? skills,
        string location,
        RemoteMode? remote,
        string searchTerm = "",
        Guid? company = null,
        string employment = "",
        SearchRadius? radius = null,
        CancellationToken cancellationToken = default,
        bool withSalaryOnly = false);

    /// <summary>Withdraws all adverts of a company.</summary>
    /// <returns>How many were withdrawn.</returns>
    /// <remarks>
    /// The special case from ADR-0027 §7: the last person with
    /// <c>role='admin'</c> has erased their account. The company is shut down,
    /// not erased — it is no natural person, and its adverts still belong to it
    /// then. But an unsupervised job advert is worse than none: applications
    /// would go to nobody.
    /// </remarks>
    Task<int> WithdrawAsync(
        TenantId company, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>The advert fetched from this source under this id, or <c>null</c>.</summary>
    Task<Job?> ByOriginAsync(Guid sourceId, string externalId, CancellationToken cancellationToken = default);

    /// <summary>Every advert fetched from this source, in any state.</summary>
    Task<IReadOnlyList<Job>> FromSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Removes an advert — only ever a draft nobody could apply to.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
