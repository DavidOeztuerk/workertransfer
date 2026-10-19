namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>A rule of the aggregate that the caller broke.</summary>
/// <remarks>
/// Thrown rather than returned, and only ever by a value object or the
/// aggregate itself. A malformed month is not an outcome the application has to
/// weigh against others — it is input that never becomes a résumé, and the one
/// boundary that turns it into a 422 is the endpoint.
/// <para>
/// Carries a <see cref="Code"/> and not only a sentence: the code is what a
/// test and a client may depend on, the sentence is for a person reading a log.
/// </para>
/// </remarks>
public sealed class ResumeRule(string code, string explanation) : Exception(explanation)
{
    /// <summary>The stable label of the broken rule.</summary>
    public string Code { get; } = code;
}

/// <summary>The codes <see cref="ResumeRule"/> uses.</summary>
public static class RuleCodes
{
    /// <summary>Not a month in the form <c>YYYY-MM</c>, or an end before its start.</summary>
    public const string Month = "invalid_month";

    /// <summary>Empty where something was required, or longer than allowed.</summary>
    public const string Text = "invalid_text";

    /// <summary>More entries than a résumé may carry.</summary>
    public const string TooMany = "too_many_entries";

    /// <summary>Two positions left open. An open one means "still there".</summary>
    public const string TwoOpen = "two_open_positions";
}
