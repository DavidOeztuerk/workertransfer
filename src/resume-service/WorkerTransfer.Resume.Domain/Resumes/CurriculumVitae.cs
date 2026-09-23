using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Resumes;

/// <summary>One résumé per person. The subject id <em>is</em> the key.</summary>
/// <remarks>
/// The aggregate knows neither a visibility nor a recipient. Who may see it is
/// not decided here but in the consent ledger, per company, and a field for it
/// would be a second truth beside the ledger (ADR-0020 §6).
/// <para>
/// There is deliberately no public switch anywhere in this type. A profile can
/// be shown to everybody; a résumé is released to one named company, asked for
/// and answered — see <c>ResumeRequest</c>.
/// </para>
/// </remarks>
public sealed class CurriculumVitae
{
    /// <summary>How many positions a résumé may hold.</summary>
    public const int MaxPositions = 40;

    /// <summary>How many education entries a résumé may hold.</summary>
    public const int MaxEducationEntries = 20;

    private CurriculumVitae(
        SubjectId subject,
        IReadOnlyList<Position> positions,
        IReadOnlyList<Education> education,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Subject = subject;
        Positions = positions;
        Education = education;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Whose résumé this is.</summary>
    public SubjectId Subject { get; }

    /// <summary>Running position first, then newest start first.</summary>
    public IReadOnlyList<Position> Positions { get; private set; }

    /// <summary>Same order, same reason.</summary>
    public IReadOnlyList<Education> Education { get; private set; }

    /// <summary>When it was first written.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The template it is typeset in.</summary>
    /// <remarks>
    /// A name, not a layout — see <see cref="ResumeTemplate" />. It stands on the
    /// résumé and not on the application, because the person chooses it once and
    /// not per company; and because they may change it afterwards without an
    /// application already sent re-dressing itself retroactively: the folder
    /// shows the template that holds at the moment of reading, and that is the
    /// person's.
    /// </remarks>
    public ResumeTemplate Template { get; private set; } = ResumeTemplate.Plain;

    /// <summary>Chooses the template.</summary>
    public void ChooseTemplate(ResumeTemplate template, DateTimeOffset now)
    {
        Template = template;
        UpdatedAt = now;
    }

    /// <summary>Writes a new one.</summary>
    /// <exception cref="ResumeRule">The entries break a rule of the whole.</exception>
    public static CurriculumVitae Create(
        SubjectId subject,
        IReadOnlyList<Position> positions,
        IReadOnlyList<Education> education,
        DateTimeOffset now)
    {
        var (checkedPositions, checkedEducation) = Checked(positions, education);

        return new CurriculumVitae(subject, checkedPositions, checkedEducation, now, now);
    }

    /// <summary>Rebuilds one that was stored. Checks nothing.</summary>
    /// <remarks>
    /// The repository's way in. Deliberately without the rules: a row written
    /// under an older rule must still be readable, and refusing to load it
    /// would hide somebody's résumé from them because the code changed.
    /// Ordering is applied, because that is derived from the data and not
    /// stored.
    /// </remarks>
    public static CurriculumVitae Restore(
        SubjectId subject,
        IReadOnlyList<Position> positions,
        IReadOnlyList<Education> education,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        ResumeTemplate template = ResumeTemplate.Plain) =>
        new(subject, Ordered(positions, p => p.End, p => p.Start),
            Ordered(education, e => e.End, e => e.Start), createdAt, updatedAt)
        {
            Template = template
        };

    /// <summary>Replaces both lists at once.</summary>
    /// <remarks>
    /// Checked in full before anything is written: a rejected form must not
    /// leave a half-changed aggregate behind.
    /// </remarks>
    /// <exception cref="ResumeRule">The entries break a rule of the whole.</exception>
    public void Update(
        IReadOnlyList<Position> positions,
        IReadOnlyList<Education> education,
        DateTimeOffset now)
    {
        var (checkedPositions, checkedEducation) = Checked(positions, education);

        Positions = checkedPositions;
        Education = checkedEducation;
        UpdatedAt = now;
    }

    /// <summary>
    /// The rules of the whole, in one place so that writing and rewriting
    /// cannot drift apart.
    /// </summary>
    private static (IReadOnlyList<Position>, IReadOnlyList<Education>) Checked(
        IReadOnlyList<Position> positions,
        IReadOnlyList<Education> education)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(education);

        if (positions.Count > MaxPositions)
        {
            throw new ResumeRule(
                RuleCodes.TooMany, $"at most {MaxPositions} positions are allowed");
        }

        if (education.Count > MaxEducationEntries)
        {
            throw new ResumeRule(
                RuleCodes.TooMany,
                $"at most {MaxEducationEntries} education entries are allowed");
        }

        // An open position means "still there". Two of them would say somebody
        // is in two places at once, and the ordering below could not decide
        // which one is now.
        if (positions.Count(position => position.IsRunning) > 1)
        {
            throw new ResumeRule(
                RuleCodes.TwoOpen,
                "only one position may stay open; open means 'still there'");
        }

        return (Ordered(positions, p => p.End, p => p.Start),
            Ordered(education, e => e.End, e => e.Start));
    }

    /// <summary>
    /// Running first, then by start descending — derived from the data instead
    /// of from a <c>sort_order</c> column.
    /// </summary>
    /// <remarks>
    /// Such a column would have to be maintained on every edit and would one
    /// day be wrong, and nothing in the interface would show it.
    /// </remarks>
    private static IReadOnlyList<T> Ordered<T>(
        IReadOnlyList<T> entries,
        Func<T, Month?> end,
        Func<T, Month> start) =>
        [.. entries
            .OrderByDescending(entry => end(entry) is null)
            .ThenByDescending(start)];
}
