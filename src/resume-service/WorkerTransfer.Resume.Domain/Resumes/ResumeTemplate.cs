namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>The template a résumé is typeset in.</summary>
/// <remarks>
/// <strong>A value, not a layout.</strong> Only the name stands here; what it
/// looks like lies in <c>web/src/styles/</c> and holds for both sides — the
/// person sees the same as the company they send the résumé to (ADR-0035). A
/// server that knew the layout would have to know it twice, and the second
/// version would be wrong at the first field.
/// <para>
/// That is why there are no colours here, no font sizes and no order of
/// sections either: all of that would be design in the wrong house.
/// </para>
/// <para>
/// The members are the labels of the Postgres enum <c>resume_template</c>
/// (Npgsql derives them from the member names), so renaming one is a schema
/// change.
/// </para>
/// </remarks>
public enum ResumeTemplate
{
    /// <summary>Without ornament. The default.</summary>
    Plain,

    /// <summary>Two columns, with a header.</summary>
    Classic,

    /// <summary>Strong headings, lots of white space.</summary>
    Modern
}
