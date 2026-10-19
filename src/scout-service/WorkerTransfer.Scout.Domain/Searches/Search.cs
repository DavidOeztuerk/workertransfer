using Noelia.Core.Identity;

namespace WorkerTransfer.Scout.Domain.Searches;

/// <summary>A saved question — and expressly no saved result.</summary>
/// <remarks>
/// <para><strong>The fourth decision of ADR-0036 stands in this type.</strong>
/// A saved search holds filters. It holds no hit, no identifier of a human
/// being, no time of a run and no number about anybody — because a saved result
/// about people goes stale against a revocation. A revocation has to take effect
/// at the next call (ADR-0013), and a hit cache would be exactly the breach of
/// that rule.</para>
///
/// <para>For the same reason there is <em>no</em> nightly run that reports
/// "new hits" to the company: that would be the same cache with an alarm clock
/// in front of it, plus a second notification beside the one to the
/// person.</para>
///
/// <para>It belongs to a <em>human being in a company</em>, not to the company
/// alone: <see cref="Subject"/> is the person column by which the erasure
/// cascade recognises this service (ADR-0027). Whoever leaves takes their saved
/// searches with them.</para>
/// </remarks>
public sealed class Search
{
    /// <summary>How long the name of a saved search may be.</summary>
    public const int MaxNameLength = 80;

    /// <summary>How many searches one human being may save per company.</summary>
    /// <remarks>
    /// A limit, so that saving does not become a store nobody tidies up.
    /// Generous enough that nobody meets it in daily work.
    /// </remarks>
    public const int MaxPerPerson = 50;

    private Search(
        Guid id,
        TenantId company,
        SubjectId subject,
        string name,
        SearchFilter filter,
        DateTimeOffset createdAt)
    {
        Id = id;
        Company = company;
        Subject = subject;
        Name = name;
        Filter = filter;
        CreatedAt = createdAt;
    }

    /// <summary>Which search.</summary>
    public Guid Id { get; }

    /// <summary>Which company it applies to.</summary>
    public TenantId Company { get; }

    /// <summary>Who saved it.</summary>
    public SubjectId Subject { get; }

    /// <summary>What the person called it.</summary>
    public string Name { get; }

    /// <summary>The question. Nothing more stands here.</summary>
    public SearchFilter Filter { get; }

    /// <summary>When it was saved.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Creates a search.</summary>
    /// <exception cref="InputError">The name is missing or too long.</exception>
    public static Search Create(
        TenantId company, SubjectId subject, string? name, SearchFilter filter, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var trimmed = (name ?? string.Empty).Trim();

        return trimmed.Length switch
        {
            0 => throw new InputError("a saved search needs a name"),
            > MaxNameLength => throw new InputError(
                $"a name may not be longer than {MaxNameLength} characters"),
            _ => new Search(Guid.CreateVersion7(), company, subject, trimmed, filter, now)
        };
    }

    /// <summary>The search as a row holds it.</summary>
    public static Search Restore(
        Guid id,
        TenantId company,
        SubjectId subject,
        string name,
        SearchFilter filter,
        DateTimeOffset createdAt) =>
        new(id, company, subject, name, filter, createdAt);
}

/// <summary>Finds and saves saved searches.</summary>
public interface ISearchStore
{
    /// <summary>Saves a search.</summary>
    Task AppendAsync(Search search, CancellationToken cancellationToken = default);

    /// <summary>This human being's searches in this company, newest first.</summary>
    /// <remarks>
    /// Per human being <em>and</em> per company: whoever works for two companies
    /// is not to see the searches of the one in the other. And whoever leaves a
    /// company does not take their searches to where they work later.
    /// </remarks>
    Task<IReadOnlyList<Search>> ForHumanAsync(
        TenantId company, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Removes a search if it belongs to this human being.</summary>
    /// <returns><c>true</c> if something was removed.</returns>
    Task<bool> RemoveAsync(
        Guid id, TenantId company, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Everything this service holds about a human being.</summary>
    /// <returns>How much was deliberately kept. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
