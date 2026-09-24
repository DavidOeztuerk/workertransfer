namespace WorkerTransfer.Applications.Application.Drafts;

/// <summary>The model's output format, read rather than guessed.</summary>
/// <remarks>
/// <c>BETREFF: …\n---\n&lt;text&gt;</c> — the marker is German because the
/// prompt that asks for it is (<see cref="Ports.CoverLetterContext.Rules"/>).
/// Robust against what models write around it: Markdown fences, leading
/// explanations, a missing dash line. A parser that only knows the ideal case
/// turns a slight deviation into an empty cover letter.
/// </remarks>
public static class CoverLetterFormat
{
    private const string Marker = "BETREFF:";

    /// <summary>Subject line and text.</summary>
    public static (string SubjectLine, string Text) Parse(string? output)
    {
        var whole = (output ?? string.Empty).Trim();
        var subjectLine = string.Empty;
        var text = whole;

        var mark = whole.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);

        if (mark >= 0)
        {
            var after = whole[(mark + Marker.Length)..];
            var dash = after.IndexOf("---", StringComparison.Ordinal);

            if (dash >= 0)
            {
                subjectLine = after[..dash];
                text = after[(dash + 3)..];
            }
            else
            {
                var lineBreak = after.IndexOf('\n');

                if (lineBreak < 0)
                {
                    // The subject line is not finished yet. Show the raw text
                    // as the body — otherwise the letter stays empty for minutes
                    // while the model types the subject.
                    subjectLine = string.Empty;
                    text = whole;
                }
                else
                {
                    subjectLine = after[..lineBreak];
                    text = after[(lineBreak + 1)..];
                }
            }
        }

        return (
            subjectLine.Trim().Trim('*', '#', ' ', '"'),
            text.Trim().Trim('`').Trim());
    }
}
