using Noelia.Core.Identity;
using WorkerTransfer.Profile.Domain.Skills;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>A person's profile — what they enter about themselves.</summary>
/// <remarks>
/// Deliberately narrow: headline, free text, location, remote readiness,
/// skills. Structured work experience is the resume, verified evidence would be
/// a service of its own; both would only be kept twice here.
/// <para>
/// <b>The profile knows no visibility</b> (ADR-0020). Whether somebody may see
/// it is answered by the consent ledger alone. A field at this place would be a
/// second truth — and the one somebody forgets to change along.
/// </para>
/// <para>
/// One profile per person: the <see cref="SubjectId"/> <em>is</em> the key.
/// There is no profile id of its own, because there would be nothing it could
/// tell apart.
/// </para>
/// </remarks>
public sealed class PersonProfile
{
    /// <summary>How long a headline may be.</summary>
    public const int MaxHeadlineLength = 120;

    /// <summary>How long the free text may be.</summary>
    public const int MaxTextLength = 4000;

    /// <summary>How long a location may be.</summary>
    public const int MaxLocationLength = 120;

    /// <summary>How long the wish to the writing aid is at most.</summary>
    /// <remarks>
    /// Considerably shorter than the text (4000), because it is something else:
    /// an instruction, not content. Whoever needs more here no longer writes a
    /// wish but sends text to a foreign provider past the profile.
    /// </remarks>
    public const int MaxWishLength = 500;

    private PersonProfile(
        SubjectId subject,
        string headline,
        string text,
        string location,
        bool remoteOk,
        SkillList skills,
        CommuteReadiness? commute,
        RelocationReadiness? relocation,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Subject = subject;
        Headline = headline;
        Text = text;
        Location = location;
        RemoteOk = remoteOk;
        Skills = skills;
        Commute = commute;
        Relocation = relocation;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Whose profile. At the same time the primary key.</summary>
    public SubjectId Subject { get; }

    /// <summary>The one line that is read first.</summary>
    public string Headline { get; private set; }

    /// <summary>What the person writes about themselves.</summary>
    public string Text { get; private set; }

    /// <summary>Where they work. Free text, because a list of places always forgets somebody.</summary>
    public string Location { get; private set; }

    /// <summary>
    /// Whether they would work remotely.
    /// </summary>
    /// <remarks>
    /// <c>false</c> means "has not said yes", not "refuses". That is why the
    /// search filters in one direction only.
    /// </remarks>
    public bool RemoteOk { get; private set; }

    /// <summary>What they can do — in their order, without an evaluation.</summary>
    public SkillList Skills { get; private set; }

    /// <summary>
    /// How far they would commute, or <c>null</c> for "said nothing".
    /// </summary>
    /// <remarks>
    /// <strong>Nullable, and it stays so.</strong> A profile without this
    /// statement is complete — "said nothing" is not "does not suit"
    /// (ADR-0041, ADR-0022 §3). Whoever says nothing gets no cross in the
    /// scout, but a dash.
    /// </remarks>
    public CommuteReadiness? Commute { get; private set; }

    /// <summary>Whether they would relocate, or <c>null</c>.</summary>
    public RelocationReadiness? Relocation { get; private set; }

    /// <summary>When the profile came into being.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it was last changed. At the same time half the page cursor.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates a profile.</summary>
    /// <exception cref="HeadlineError">The headline is missing or too long.</exception>
    /// <exception cref="TextError">The text is too long.</exception>
    /// <exception cref="LocationError">The location is too long.</exception>
    public static PersonProfile Create(
        SubjectId subject,
        string headline,
        string text,
        string location,
        bool remoteOk,
        SkillList skills,
        DateTimeOffset now,
        CommuteReadiness? commute = null,
        RelocationReadiness? relocation = null)
    {
        var (checkedHeadline, checkedText, checkedLocation) = Check(headline, text, location);

        return new PersonProfile(
            subject, checkedHeadline, checkedText, checkedLocation, remoteOk,
            skills ?? SkillList.Empty,
            commute, relocation, now, now);
    }

    /// <summary>Rebuilds a stored profile.</summary>
    /// <remarks>
    /// The only entrance for an existing row, and it does <em>not</em> check
    /// again. A stored row was valid when it came into being; refusing it on
    /// reading would mean taking somebody's profile away because an upper
    /// limit has changed.
    /// </remarks>
    public static PersonProfile Restore(
        SubjectId subject,
        string headline,
        string text,
        string location,
        bool remoteOk,
        SkillList skills,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        CommuteReadiness? commute = null,
        RelocationReadiness? relocation = null) =>
        new(subject, headline, text, location, remoteOk,
            skills ?? SkillList.Empty,
            commute, relocation, createdAt, updatedAt);

    /// <summary>Changes all fields at once.</summary>
    /// <remarks>
    /// Check completely first, then write: a refused form must not leave a
    /// half-changed aggregate behind.
    /// </remarks>
    /// <exception cref="HeadlineError">The headline is missing or too long.</exception>
    /// <exception cref="TextError">The text is too long.</exception>
    /// <exception cref="LocationError">The location is too long.</exception>
    public void Change(
        string headline,
        string text,
        string location,
        bool remoteOk,
        SkillList skills,
        DateTimeOffset now,
        CommuteReadiness? commute = null,
        RelocationReadiness? relocation = null)
    {
        var (checkedHeadline, checkedText, checkedLocation) = Check(headline, text, location);

        Headline = checkedHeadline;
        Text = checkedText;
        Location = checkedLocation;
        RemoteOk = remoteOk;
        Skills = skills ?? SkillList.Empty;
        // Without a statement the statement is DELETED and not kept: the form
        // always sends every field, and a "null means: leave it as it was"
        // would make taking a statement back impossible.
        Commute = commute;
        Relocation = relocation;
        UpdatedAt = now;
    }

    /// <summary>
    /// Checks and trims the three text fields together.
    /// </summary>
    /// <remarks>
    /// Together, so that creating and changing cannot drift apart.
    /// </remarks>
    private static (string Headline, string Text, string Location) Check(
        string headline, string text, string location)
    {
        var trimmedHeadline = (headline ?? string.Empty).Trim();

        if (trimmedHeadline.Length == 0)
        {
            throw new HeadlineError("must not be empty");
        }

        if (trimmedHeadline.Length > MaxHeadlineLength)
        {
            throw new HeadlineError(
                $"may have at most {MaxHeadlineLength} characters");
        }

        var trimmedText = (text ?? string.Empty).Trim();

        if (trimmedText.Length > MaxTextLength)
        {
            throw new TextError();
        }

        var trimmedLocation = (location ?? string.Empty).Trim();

        return trimmedLocation.Length > MaxLocationLength
            ? throw new LocationError()
            : (trimmedHeadline, trimmedText, trimmedLocation);
    }
}
