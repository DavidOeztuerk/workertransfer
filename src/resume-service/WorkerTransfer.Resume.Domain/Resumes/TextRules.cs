namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>The two text rules every field of a résumé obeys.</summary>
internal static class TextRules
{
    internal const int MaxNameLength = 160;

    internal const int MaxDescriptionLength = 2000;

    internal static string Checked(string field, string? value, bool required, int limit)
    {
        var clean = (value ?? string.Empty).Trim();

        if (required && clean.Length == 0)
        {
            throw new ResumeRule(RuleCodes.Text, $"{field} must not be empty");
        }

        return clean.Length > limit
            ? throw new ResumeRule(
                RuleCodes.Text, $"{field} exceeds {limit} characters")
            : clean;
    }

    /// <summary>An end before its start is not a span.</summary>
    /// <remarks>
    /// The same month is allowed: a one-month probation is short, not a typo.
    /// </remarks>
    internal static void CheckSpan(Month start, Month? end)
    {
        if (end is not null && end < start)
        {
            throw new ResumeRule(
                RuleCodes.Month, $"the end {end} lies before the start {start}");
        }
    }
}
