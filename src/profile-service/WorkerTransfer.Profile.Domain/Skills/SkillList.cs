using WorkerTransfer.Skills;

namespace WorkerTransfer.Profile.Domain.Skills;

/// <summary>Too many skills, or one too long.</summary>
/// <param name="reason">What was too much.</param>
public sealed class SkillError(string reason) : InputError(reason);

/// <summary>Skills as a normalised list that keeps its order.</summary>
/// <remarks>
/// The order is the person's: they typed the most important one first, and an
/// ordering by alphabet or by frequency would be somebody else's statement
/// about what matters about them.
/// <para>
/// <b>Rename first, then deduplicate</b> (ADR-0023). The other way round,
/// "Postgres" and "PostgreSQL" would stand in the list twice — and the
/// comparison in the browser would show the same human being the same skill
/// twice.
/// </para>
/// </remarks>
public sealed class SkillList
{
    /// <summary>How many skills a profile carries.</summary>
    /// <remarks>
    /// Counted <em>after</em> deduplicating: otherwise a list with thirty-one
    /// times "Python" would turn somebody away, although it becomes a single
    /// skill.
    /// </remarks>
    public const int MaxEntries = 30;

    /// <summary>How long a single skill may be.</summary>
    /// <inheritdoc cref="SkillLimits.MaxLength" />
    public const int MaxLength = SkillLimits.MaxLength;

    /// <summary>The empty list — somebody who entered nothing.</summary>
    /// <remarks>
    /// "Said nothing" is not "can do nothing". The comparison shows them no
    /// balance, not "0 of 3".
    /// </remarks>
    public static SkillList Empty { get; } = new([]);

    private SkillList(IReadOnlyList<string> values) => Values = values;

    /// <summary>The skills, canonical and deduplicated.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Builds the list from what somebody typed.</summary>
    /// <param name="raw">The input, unchanged.</param>
    /// <exception cref="SkillError">
    /// A skill is too long, or after deduplicating there are too many.
    /// </exception>
    public static SkillList From(IEnumerable<string>? raw) => Build(raw, enforceLimits: true);

    /// <summary>Builds the list from a stored row.</summary>
    /// <remarks>
    /// Does <em>not</em> check the limits. A stored row was valid when it came
    /// into being; refusing it on reading would mean taking somebody's profile
    /// away because somebody else lowered an upper limit later.
    /// <para>
    /// It is renamed and deduplicated anyway: that way a new entry in the
    /// vocabulary also takes effect on rows written before it — without a data
    /// migration.
    /// </para>
    /// </remarks>
    /// <param name="stored">What stands in the column.</param>
    public static SkillList Restore(IEnumerable<string>? stored) =>
        Build(stored, enforceLimits: false);

    private static SkillList Build(IEnumerable<string>? raw, bool enforceLimits)
    {
        if (raw is null)
        {
            return Empty;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clean = new List<string>();

        foreach (var entry in Vocabulary.CanonicaliseAll(raw))
        {
            if (enforceLimits && entry.Length > MaxLength)
            {
                throw new SkillError(
                    $"A skill may have at most {MaxLength} characters.");
            }

            // Upper and lower case is no second skill. The first spelling wins
            // — that is how the person entered it.
            if (seen.Add(entry))
            {
                clean.Add(entry);
            }
        }

        return enforceLimits && clean.Count > MaxEntries
            ? throw new SkillError($"At most {MaxEntries} skills are allowed.")
            : new SkillList(clean);
    }
}
