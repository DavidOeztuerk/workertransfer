namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>School or vocational training — both education, not the same one.</summary>
/// <remarks>
/// Stored in the jsonb column and sent on the wire as the words
/// <c>"ausbildung"</c> and <c>"schule"</c>, never as these member names: the
/// words are a contract with the stored rows and with the web app, the names
/// are not.
/// </remarks>
public enum EducationKind
{
    /// <summary>Apprenticeship, retraining, vocational qualification.</summary>
    Vocational,

    /// <summary>General school.</summary>
    School
}

/// <summary>One stretch of education.</summary>
/// <remarks>
/// The qualification is optional and stays optional: somebody who broke off a
/// degree still spent those years somewhere, and a required field would force
/// them either to lie or to leave the whole entry out.
/// </remarks>
public sealed record Education
{
    private Education(
        string institution,
        string qualification,
        Month start,
        Month? end,
        EducationKind kind)
    {
        Institution = institution;
        Qualification = qualification;
        Start = start;
        End = end;
        Kind = kind;
    }

    /// <summary>Where.</summary>
    public string Institution { get; }

    /// <summary>What came of it, where anything did.</summary>
    public string Qualification { get; }

    /// <summary>From when.</summary>
    public Month Start { get; }

    /// <summary><c>null</c> means "still studying".</summary>
    public Month? End { get; }

    /// <summary>School or vocational training.</summary>
    public EducationKind Kind { get; }

    /// <summary>Whether it is still running.</summary>
    public bool IsRunning => End is null;

    /// <summary>Builds one, or refuses.</summary>
    /// <exception cref="ResumeRule">A field or the span is not valid.</exception>
    public static Education Create(
        string institution,
        string qualification,
        Month start,
        Month? end = null,
        EducationKind kind = EducationKind.Vocational)
    {
        ArgumentNullException.ThrowIfNull(start);

        TextRules.CheckSpan(start, end);

        return new Education(
            TextRules.Checked("institution", institution, required: true, TextRules.MaxNameLength),
            TextRules.Checked("qualification", qualification, required: false, TextRules.MaxNameLength),
            start,
            end,
            kind);
    }
}
