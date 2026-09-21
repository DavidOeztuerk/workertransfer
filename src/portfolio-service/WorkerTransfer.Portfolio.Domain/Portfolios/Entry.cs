using WorkerTransfer.Skills;

using System.Text.RegularExpressions;

namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>A piece of work somebody wants shown.</summary>
/// <remarks>
/// Carries <b>no visibility level</b>. A portfolio is a shop window, not a
/// filing cabinet: it is released as a whole, and the fine grain sits in the
/// decision about what goes into it. What may not be shown does not belong in
/// it — a level per entry would be a second truth beside the ledger
/// (ADR-0020 §6).
/// </remarks>
public sealed partial record Entry
{
    /// <summary>How long a heading may be.</summary>
    public const int MaxTitleLength = 160;

    /// <summary>How long a summary may be.</summary>
    public const int MaxSummaryLength = 1000;

    /// <summary>How long a role may be.</summary>
    public const int MaxRoleLength = 160;

    /// <summary>How long a link may be.</summary>
    public const int MaxLinkLength = 2000;

    /// <summary>How long an attachment name may be.</summary>
    public const int MaxAttachmentNameLength = 80;

    /// <summary>How many technologies one piece of work may name.</summary>
    /// <remarks>
    /// Twelve, the same number as at a CV position: whoever lists thirty says
    /// nothing about a single piece of work any more.
    /// </remarks>
    public const int MaxTechnologies = 12;

    /// <summary>Before this year nobody here published anything.</summary>
    public const int EarliestYear = 1900;

    /// <summary>
    /// These two schemes and no others.
    /// </summary>
    /// <remarks>
    /// The link gets clicked by strangers, and the field ends up in a browser.
    /// A list of allowed schemes is not purism here but the place where a
    /// <c>javascript:</c> is caught.
    /// </remarks>
    public static IReadOnlySet<string> AllowedSchemes { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "http", "https" };

    private Entry(
        string title,
        string summary,
        string? link,
        string role,
        int? year,
        string? attachment,
        IReadOnlyList<string> technologies)
    {
        Title = title;
        Summary = summary;
        Link = link;
        Role = role;
        Year = year;
        Attachment = attachment;
        Technologies = technologies;
    }

    /// <summary>What it is about.</summary>
    public string Title { get; }

    /// <summary>What was worked with — named by the person themselves.</summary>
    /// <remarks>
    /// <strong>A naming, not a derivation.</strong> Nobody reads out of a
    /// title which tools stand behind it; here stands what the person wrote,
    /// canonicalised by the same vocabulary as in the profile (ADR-0023:
    /// renames, never infers).
    /// <para>
    /// It does not make the work searchable. A skill becomes searchable only
    /// once it stands in the PROFILE — it gets there with a click, never by
    /// itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Technologies { get; }

    /// <summary>What the person wrote about it themselves.</summary>
    public string Summary { get; }

    /// <summary><c>null</c> means: no link.</summary>
    public string? Link { get; }

    /// <summary>What they did in it.</summary>
    public string Role { get; }

    /// <summary><c>null</c> means: no year given.</summary>
    public int? Year { get; }

    /// <summary>
    /// The name of an uploaded file, assigned by the server.
    /// </summary>
    /// <remarks>
    /// No path and no URL: at delivery time it is combined with the
    /// <c>subject_id</c> into a storage key, and exactly that structure keeps
    /// anybody from reaching somebody else's file with a foreign name.
    /// </remarks>
    public string? Attachment { get; }

    /// <summary>Builds an entry, or refuses it.</summary>
    /// <param name="now">
    /// For the upper bound of the year. Passed in instead of fetching a clock:
    /// a value object with a hidden present cannot be pinned down in a test.
    /// </param>
    public static Entry From(
        string title,
        DateTimeOffset now,
        string summary = "",
        string? link = null,
        string role = "",
        int? year = null,
        string? attachment = null,
        IEnumerable<string>? technologies = null) =>
        new(
            Text("Der Titel", title, required: true, MaxTitleLength),
            Text("Die Zusammenfassung", summary, required: false, MaxSummaryLength),
            Checked(link),
            Text("Die Rolle", role, required: false, MaxRoleLength),
            YearNumber(year, now),
            AttachmentName(attachment),
            Canonicalised(technologies ?? []));

    /// <summary>Canonicalise first, deduplicate second — the order carries.</summary>
    /// <remarks>
    /// The other way round "postgres" and "PostgreSQL" would stand there as two
    /// entries and only afterwards both become "PostgreSQL" (ADR-0023).
    /// </remarks>
    private static IReadOnlyList<string> Canonicalised(IEnumerable<string> raw)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();

        foreach (var single in raw)
        {
            var checkedValue = Text(
                "Die Technologie", single, required: false, SkillLimits.MaxLength);

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
            ? throw new TextError(
                "Die Technologien",
                $"sind mehr als {MaxTechnologies}")
            : values;
    }

    private static string Text(string field, string? value, bool required, int limit)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (required && trimmed.Length == 0)
        {
            throw new TextError(field, "darf nicht leer sein");
        }

        return trimmed.Length > limit
            ? throw new TextError(field, $"ist länger als {limit} Zeichen")
            : trimmed;
    }

    private static string? Checked(string? link)
    {
        var trimmed = link?.Trim();

        // Empty and "not given" are the same thing. An empty string would
        // later be rendered as a link and lead nowhere.
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > MaxLinkLength)
        {
            throw new LinkError($"Der Link ist länger als {MaxLinkLength} Zeichen.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
        {
            throw new LinkError("Das ist keine vollständige Adresse.");
        }

        if (!AllowedSchemes.Contains(parsed.Scheme))
        {
            throw new LinkError("Nur http- und https-Links sind erlaubt.");
        }

        return string.IsNullOrEmpty(parsed.Host)
            ? throw new LinkError("Dem Link fehlt der Rechnername.")
            : trimmed;
    }

    private static int? YearNumber(int? year, DateTimeOffset now)
    {
        if (year is not { } value)
        {
            return null;
        }

        // Next year is allowed: something may be about to appear. Further
        // into the future is a typo, not a plan.
        return value < EarliestYear || value > now.Year + 1
            ? throw new YearError(
                $"Ein Jahr muss zwischen {EarliestYear} und {now.Year + 1} liegen.")
            : value;
    }

    /// <summary>
    /// A name only, never a path.
    /// </summary>
    /// <remarks>
    /// The client gets it back from the upload and sends it along at save
    /// time. If a path were allowed, somebody could point out of their own
    /// directory with <c>../</c> — the store catches that too, but a check at
    /// the boundary is cheaper than an exception down in the depths.
    /// </remarks>
    private static string? AttachmentName(string? attachment)
    {
        var trimmed = attachment?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > MaxAttachmentNameLength || !AttachmentPattern().IsMatch(trimmed)
            ? throw new AttachmentError()
            : trimmed;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex AttachmentPattern();
}
