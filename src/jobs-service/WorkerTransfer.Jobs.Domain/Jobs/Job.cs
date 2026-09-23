using Noelia.Core.Identity;

namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>Where a job advert stands.</summary>
/// <remarks>
/// The member names are the wire words too: the API writes
/// <c>State.ToString().ToLowerInvariant()</c>, so <c>Draft</c> goes out as
/// <c>"draft"</c>. Renaming a member changes what the UI receives.
/// </remarks>
public enum JobState
{
    /// <summary>Written, not published.</summary>
    Draft,

    /// <summary>Visible.</summary>
    Published,

    /// <summary>Withdrawn or filled.</summary>
    Closed
}

/// <summary>How much remote work is possible.</summary>
/// <remarks>
/// An enumeration, not a boolean. "Remote possible?" is the question everybody
/// asks, and "yes/no" answers it wrongly: hybrid is the commonest case and not
/// an intermediate step of true.
/// <para>
/// The member names are the wire words (<c>"none"</c>, <c>"hybrid"</c>,
/// <c>"full"</c>) — the API lower-cases them on the way out.
/// </para>
/// </remarks>
public enum RemoteMode
{
    /// <summary>On site.</summary>
    None,

    /// <summary>Partly.</summary>
    Hybrid,

    /// <summary>Entirely.</summary>
    Full
}

/// <summary>Which kind of employment.</summary>
public enum EmploymentKind
{
    FullTime,
    PartTime,
    Contract,
    Internship
}

/// <summary>A job advert.</summary>
/// <remarks>
/// It belongs to a company, not to a human being. That is why this service is
/// <b>not a recipient of the erasure cascade</b> (ADR-0027 §2): it holds nothing
/// about a natural person, and an erasure command to it would be an endpoint
/// that says "done" without ever doing anything.
/// <para>
/// There is no score here, no ranking and no fit. How well somebody suits a job
/// is computed in the browser, shown to the person and never to the company —
/// and it orders <em>jobs</em>, not people (ADR-0022).
/// </para>
/// </remarks>
public sealed class Job
{
    /// <summary>How long a title may be.</summary>
    public const int MaxTitleLength = 160;

    /// <summary>How long a description may be.</summary>
    public const int MaxDescriptionLength = 20000;

    /// <summary>How long a location may be.</summary>
    public const int MaxLocationLength = 160;

    /// <summary>How long a postal code may be.</summary>
    /// <remarks>
    /// Ten, not five: Germany has five digits, Austria and Switzerland four,
    /// and enough people write "D-10115" that a field refusing it causes more
    /// trouble than it creates order. Only the digits are read anyway
    /// (<c>PlaceLookup</c>).
    /// </remarks>
    public const int MaxPostalCodeLength = 10;

    /// <summary>How long the wish to the drafting aid may be at most.</summary>
    /// <remarks>
    /// The same number as in profile-service, and for the same reason: the wish
    /// is an instruction, not content. The description may have 20000
    /// characters — it is the advert. The wish is not.
    /// </remarks>
    public const int MaxWishLength = 500;

    private Job(
        Guid id,
        TenantId company,
        string title,
        string description,
        string location,
        string postalCode,
        RemoteMode remote,
        EmploymentKind employment,
        SkillList skills,
        JobState state,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt)
    {
        Id = id;
        Company = company;
        Title = title;
        Description = description;
        Location = location;
        PostalCode = postalCode;
        Remote = remote;
        Employment = employment;
        Skills = skills;
        State = state;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        PublishedAt = publishedAt;
    }

    /// <summary>Which advert.</summary>
    public Guid Id { get; }

    /// <summary>Whom it belongs to.</summary>
    public TenantId Company { get; }

    /// <summary>What it is about.</summary>
    public string Title { get; private set; }

    /// <summary>What the company wrote itself.</summary>
    public string Description { get; private set; }

    /// <summary>Where.</summary>
    public string Location { get; private set; }

    /// <summary>The postal code. Empty when none was given.</summary>
    /// <remarks>
    /// <strong>A field of its own and not part of <see cref="Location"/>.</strong>
    /// The place name is free text and may be ambiguous — there are twenty
    /// places called "Neustadt". The postal code is not, and only it makes the
    /// radius search around a small place reliable. Without it the advert stays
    /// findable but drops out of a radius search when its place name is not
    /// unique — and the searching person is told so (ADR-0032), not kept in the
    /// dark.
    /// </remarks>
    public string PostalCode { get; private set; }

    /// <summary>How much remote.</summary>
    public RemoteMode Remote { get; private set; }

    /// <summary>Which employment.</summary>
    public EmploymentKind Employment { get; private set; }

    /// <summary>What is asked for.</summary>
    public SkillList Skills { get; private set; }

    /// <summary>Where it stands.</summary>
    public JobState State { get; private set; }

    /// <summary>When it was written.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary><c>null</c> as long as it was never published.</summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Writes a new advert. It begins as a draft.</summary>
    /// <remarks>
    /// Never visible at once. Whoever writes an advert should be able to read
    /// it before applications come in on it.
    /// </remarks>
    public static Job Create(
        TenantId company,
        string title,
        string description,
        string location,
        string postalCode,
        RemoteMode remote,
        EmploymentKind employment,
        SkillList skills,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(), company,
            Text("The title", title, required: true, MaxTitleLength),
            Text("The description", description, required: true, MaxDescriptionLength),
            Text("The location", location, required: false, MaxLocationLength),
            Text("The postal code", postalCode, required: false, MaxPostalCodeLength),
            remote, employment, skills, JobState.Draft, now, now, null);

    /// <summary>The advert as a row holds it.</summary>
    public static Job Restore(
        Guid id,
        TenantId company,
        string title,
        string description,
        string location,
        string postalCode,
        RemoteMode remote,
        EmploymentKind employment,
        SkillList skills,
        JobState state,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? publishedAt) =>
        new(id, company, title, description, location, postalCode, remote, employment,
            skills, state, createdAt, updatedAt, publishedAt);

    /// <summary>Changes the content.</summary>
    /// <exception cref="TransitionNotAllowed">Nobody changes a closed advert any more.</exception>
    public void Change(
        string title,
        string description,
        string location,
        string postalCode,
        RemoteMode remote,
        EmploymentKind employment,
        SkillList skills,
        DateTimeOffset now)
    {
        // Changing a closed advert would mean changing the text people applied
        // to.
        if (State is JobState.Closed)
        {
            throw new TransitionNotAllowed(State, JobState.Draft);
        }

        Title = Text("The title", title, required: true, MaxTitleLength);
        Description = Text(
            "The description", description, required: true, MaxDescriptionLength);
        Location = Text("The location", location, required: false, MaxLocationLength);
        PostalCode = Text(
            "The postal code", postalCode, required: false, MaxPostalCodeLength);
        Remote = remote;
        Employment = employment;
        Skills = skills;
        UpdatedAt = now;
    }

    /// <summary>Makes it visible.</summary>
    /// <remarks>
    /// A closed one may go out again too: a position that has to be filled anew
    /// is the same text.
    /// </remarks>
    public void Publish(DateTimeOffset now)
    {
        if (State is JobState.Published)
        {
            return;
        }

        State = JobState.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
    }

    /// <summary>Withdraws it.</summary>
    /// <remarks>
    /// A draft can be closed too — it is then discarded, and that is a
    /// legitimate outcome.
    /// </remarks>
    public void Close(DateTimeOffset now)
    {
        State = JobState.Closed;
        UpdatedAt = now;
    }

    private static string Text(string field, string? value, bool required, int limit)
    {
        var trimmed = (value ?? string.Empty).Trim();

        if (required && trimmed.Length == 0)
        {
            throw new TextError(field, "must not be empty");
        }

        return trimmed.Length > limit
            ? throw new TextError(field, $"is longer than {limit} characters")
            : trimmed;
    }
}
