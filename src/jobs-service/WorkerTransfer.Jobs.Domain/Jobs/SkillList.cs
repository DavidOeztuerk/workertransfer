using WorkerTransfer.Skills;

namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>What the job asks for — canonicalised and order-preserving.</summary>
/// <remarks>
/// A second implementation of the same rules as in <c>profile-service</c>, and
/// deliberately so: skills on a job are a model of this service and stay with
/// it (ADR-0004). Shared is only what <em>has</em> to be shared — the vocabulary
/// and the maximum length, both in <c>WorkerTransfer.Skills</c>.
/// <para>
/// Comparing does not happen here anyway but in the browser, and without regard
/// to upper and lower case.
/// </para>
/// </remarks>
public sealed class SkillList
{
    /// <summary>
    /// Twenty. Beyond that it is a wish list nobody reads through anyway.
    /// </summary>
    /// <remarks>
    /// Fewer than a profile holds, and that is fine: the job may be smaller,
    /// not longer — see <see cref="MaxLength"/>.
    /// </remarks>
    public const int MaxEntries = 20;

    /// <inheritdoc cref="SkillLimits.MaxLength" />
    public const int MaxLength = SkillLimits.MaxLength;

    private SkillList(IReadOnlyList<string> values) => Values = values;

    /// <summary>Nothing named.</summary>
    public static SkillList Empty { get; } = new([]);

    /// <summary>The requirements, in the order in which they came.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>Builds the list, or refuses it.</summary>
    /// <exception cref="SkillError">Too many, or one too long.</exception>
    public static SkillList From(IEnumerable<string>? raw) => Build(raw, enforceLimits: true);

    /// <summary>
    /// The list as a row holds it.
    /// </summary>
    /// <remarks>
    /// Without checking the number: a stored row that breaks a limit lowered
    /// later should stay readable. Otherwise lowering a number would be a loss
    /// of data.
    /// </remarks>
    public static SkillList Restore(IEnumerable<string>? stored) =>
        Build(stored, enforceLimits: false);

    private static SkillList Build(IEnumerable<string>? raw, bool enforceLimits)
    {
        if (raw is null)
        {
            return Empty;
        }

        List<string> cleaned = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // Rename first, THEN de-duplicate (ADR-0023). The other way round
        // "Postgres, PostgreSQL" would become the same entry twice, and the
        // advert would count one requirement double.
        foreach (var entry in Vocabulary.CanonicaliseAll(raw))
        {
            var value = entry.Trim();

            if (value.Length == 0)
            {
                continue;
            }

            if (enforceLimits && value.Length > MaxLength)
            {
                throw new SkillError(
                    $"A skill may have at most {MaxLength} characters.");
            }

            // Upper and lower case is no second requirement. The first spelling
            // wins — that is how the company wrote it, and that is how it later
            // stands in the list the person sees.
            if (seen.Add(value))
            {
                cleaned.Add(value);
            }
        }

        // De-duplicate first, then count.
        return enforceLimits && cleaned.Count > MaxEntries
            ? throw new SkillError($"At most {MaxEntries} skills are allowed.")
            : new SkillList(cleaned);
    }
}
