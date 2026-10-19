using WorkerTransfer.Skills;

namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>One position: a real employer, with a span.</summary>
/// <remarks>
/// This is what separates a résumé from a profile. A profile is a notice board
/// somebody wrote about themselves; this names the company they work for right
/// now, and it is exactly what a current employer must not be shown.
/// </remarks>
public sealed record Position
{
    /// <summary>How many technologies a position may name.</summary>
    /// <remarks>
    /// Twelve. Whoever lists thirty says nothing about a single job any more —
    /// and the limit also keeps the suggestion list on the profile from turning
    /// into a wall of words.
    /// </remarks>
    public const int MaxTechnologies = 12;

    private Position(
        string employer,
        string title,
        Month start,
        Month? end,
        string description,
        IReadOnlyList<string> technologies)
    {
        Employer = employer;
        Title = title;
        Start = start;
        End = end;
        Description = description;
        Technologies = technologies;
    }

    /// <summary>Who they worked for.</summary>
    public string Employer { get; }

    /// <summary>What they did there.</summary>
    public string Title { get; }

    /// <summary>From when.</summary>
    public Month Start { get; }

    /// <summary><c>null</c> means "still there" — never "unknown".</summary>
    public Month? End { get; }

    /// <summary>Free text the person wrote about their own work.</summary>
    public string Description { get; }

    /// <summary>What was worked with there — named by the person themselves.</summary>
    /// <remarks>
    /// <strong>A naming, not a derivation.</strong> Nobody computes from a job
    /// title which tools belong to it; what stands here is what the person
    /// wrote, canonicalised by the same vocabulary as on the profile (ADR-0023:
    /// it renames, it never infers).
    /// <para>
    /// It does NOT make this position searchable. A résumé is released one
    /// company at a time (ADR-0020), and what stands here belongs to the company
    /// that may read it. A skill becomes searchable only once it stands on the
    /// PROFILE — it gets there with one click, not by itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Technologies { get; }

    /// <summary>Whether this is the position they hold now.</summary>
    public bool IsRunning => End is null;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="ResumeRule">A field or the span is not valid.</exception>
    public static Position Create(
        string employer,
        string title,
        Month start,
        Month? end = null,
        string description = "",
        IEnumerable<string>? technologies = null)
    {
        ArgumentNullException.ThrowIfNull(start);

        TextRules.CheckSpan(start, end);

        return new Position(
            TextRules.Checked("employer", employer, required: true, TextRules.MaxNameLength),
            TextRules.Checked("title", title, required: true, TextRules.MaxNameLength),
            start,
            end,
            TextRules.Checked(
                "description", description, required: false, TextRules.MaxDescriptionLength),
            Canonicalised(technologies ?? []));
    }

    /// <summary>Canonicalise first, then deduplicate — the order is load-bearing.</summary>
    /// <remarks>
    /// The other way round, "postgres" and "PostgreSQL" would stand as two
    /// entries and only afterwards both become "PostgreSQL" (ADR-0023).
    /// </remarks>
    private static IReadOnlyList<string> Canonicalised(IEnumerable<string> raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();

        foreach (var single in raw)
        {
            var checkedValue = TextRules.Checked(
                "technology", single, required: false, SkillLimits.MaxLength);

            if (checkedValue.Length == 0)
            {
                continue;
            }

            var canonical = Vocabulary.Canonicalise(checkedValue);

            if (seen.Add(canonical))
            {
                values.Add(canonical);
            }
        }

        return values.Count > MaxTechnologies
            ? throw new ResumeRule(
                RuleCodes.TooMany,
                $"a position names at most {MaxTechnologies} technologies")
            : values;
    }
}
