using WorkerTransfer.Skills;

namespace WorkerTransfer.Scout.Domain.Searches;

/// <summary>An input this service cannot accept.</summary>
/// <remarks>
/// The message names the <em>rule</em>, never the value. It is logged (Noelia's
/// <c>ValidationBehavior</c> does that), and a search term is one human being's
/// input about another.
/// </remarks>
/// <param name="reason">Which rule was broken.</param>
public class InputError(string reason) : Exception(reason);

/// <summary>What a company searches for — and exclusively for what was named.</summary>
/// <remarks>
/// <para><strong>The third condition of ADR-0036 stands here as a type.</strong>
/// A filter carries only words a person typed into their profile themselves
/// (provenance class <em>named</em>, ADR-0033). Evidence — GitHub topics,
/// languages, technologies on a position — is <em>fetched to</em> the hit and
/// deliberately has no place here: evidence is a statement about an artefact,
/// and whoever searched by it would silently turn it into one about the human
/// being.</para>
///
/// <para><strong>The words are an OR, not an AND</strong>, and that is no
/// loosening: under AND every hit names everything searched for, every tick
/// would be set, and "which skill is missing" would have no answer (ADR-0036
/// decision 2). Whoever demands everything reads it off the ticks — and decides
/// themselves instead of getting a number that decides for them.</para>
///
/// <para>Canonicalise first, deduplicate second (ADR-0023, the order carries
/// weight): the other way round, "Postgres" and "PostgreSQL" would stand in the
/// tick list twice, and the same human being would have two ticks for the same
/// skill.</para>
/// </remarks>
public sealed class SearchFilter
{
    /// <summary>How many skills one search may name.</summary>
    /// <remarks>
    /// The same consideration as the page cap: without a limit a caller builds an
    /// arbitrarily expensive query with a single address line — every skill is
    /// another condition on a foreign database.
    /// </remarks>
    public const int MaxWords = 10;

    /// <summary>How long a single word may be.</summary>
    public const int MaxWordLength = SkillLimits.MaxLength;

    /// <summary>How long the location may be.</summary>
    public const int MaxLocationLength = 120;

    /// <summary>The empty filter — "show me who wants to be shown".</summary>
    /// <remarks>
    /// Expressly allowed. A search without a word is no empty question: it names
    /// exactly nobody and still shows only whoever has released.
    /// </remarks>
    public static SearchFilter Empty { get; } = new([], string.Empty, false);

    private SearchFilter(IReadOnlyList<string> namedWords, string location, bool remoteOnly)
    {
        NamedWords = namedWords;
        Location = location;
        RemoteOnly = remoteOnly;
    }

    /// <summary>The searched words, canonical and deduplicated.</summary>
    public IReadOnlyList<string> NamedWords { get; }

    /// <summary>Partial text on the location, or empty.</summary>
    public string Location { get; }

    /// <summary>
    /// Only whoever expressly ticked "remote possible".
    /// </summary>
    /// <remarks>
    /// One direction only. <c>false</c> means "did not say yes", not "declines" —
    /// a filter on the opposite would exclude whoever simply ticked nothing.
    /// </remarks>
    public bool RemoteOnly { get; }

    /// <summary>Builds the filter from what somebody typed.</summary>
    /// <exception cref="InputError">Too many words, one too long, a location too long.</exception>
    public static SearchFilter From(IEnumerable<string>? words, string? location, bool remoteOnly)
    {
        var typed = (words ?? [])
            .Select(word => word?.Trim() ?? string.Empty)
            .Where(word => word.Length > 0)
            .ToArray();

        if (typed.Any(word => word.Length > MaxWordLength))
        {
            throw new InputError(
                $"a search word may not be longer than {MaxWordLength} characters");
        }

        // Canonicalise first, deduplicate second (ADR-0023).
        var canonical = Vocabulary.CanonicaliseAll(typed)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (canonical.Length > MaxWords)
        {
            throw new InputError(
                $"a search may name at most {MaxWords} skills");
        }

        var locationValue = (location ?? string.Empty).Trim();

        return locationValue.Length > MaxLocationLength
            ? throw new InputError(
                $"a location may not be longer than {MaxLocationLength} characters")
            : new SearchFilter(canonical, locationValue, remoteOnly);
    }

    /// <summary>The filter as a saved row holds it.</summary>
    /// <remarks>
    /// Without checking the limits. A saved search was valid when it was created;
    /// refusing it on reading would take somebody's saved search away because a
    /// cap was lowered later. It is canonicalised all the same — a new entry in
    /// the vocabulary thus reaches older rows too, without a data migration.
    /// </remarks>
    public static SearchFilter Restore(IEnumerable<string>? words, string? location, bool remoteOnly) =>
        new(
            [.. Vocabulary.CanonicaliseAll(words ?? []).Distinct(StringComparer.OrdinalIgnoreCase)],
            (location ?? string.Empty).Trim(),
            remoteOnly);
}
