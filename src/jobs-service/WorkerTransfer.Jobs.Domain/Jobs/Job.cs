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
        DateTimeOffset? publishedAt,
        SalaryBand? salary,
        JobOrigin? origin = null)
    {
        Origin = origin;
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
        Salary = salary;
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

    /// <summary>What the job pays, as the company states it — or <c>null</c>: not stated.</summary>
    public SalaryBand? Salary { get; private set; }

    /// <summary>Where it stands.</summary>
    public JobState State { get; private set; }

    /// <summary>Where the advert was fetched from, or <c>null</c>: written on the platform (ADR-0052).</summary>
    /// <remarks>
    /// A fetched advert mirrors its source: it is not edited here, because the
    /// next fetch would overwrite the edit without anybody noticing.
    /// </remarks>
    public JobOrigin? Origin { get; private set; }

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
        DateTimeOffset now,
        SalaryBand? salary = null) =>
        new(
            Guid.CreateVersion7(), company,
            Text("The title", title, required: true, MaxTitleLength),
            Text("The description", description, required: true, MaxDescriptionLength),
            Text("The location", location, required: false, MaxLocationLength),
            Text("The postal code", postalCode, required: false, MaxPostalCodeLength),
            remote, employment, skills, JobState.Draft, now, now, null, salary);

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
        DateTimeOffset? publishedAt,
        SalaryBand? salary = null,
        JobOrigin? origin = null) =>
        new(id, company, title, description, location, postalCode, remote, employment,
            skills, state, createdAt, updatedAt, publishedAt, salary, origin);

    /// <summary>An advert fetched from a source — a draft until somebody or the source's mode publishes it.</summary>
    /// <remarks>
    /// Over-long texts are shortened, not refused: the source decides its
    /// length, and refusing would leave the advert out without the company
    /// learning why.
    /// </remarks>
    public static Job Import(TenantId company, JobOrigin origin, ExternalAdvert advert, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(advert);

        var job = Create(
            company,
            Shorten(advert.Title, MaxTitleLength),
            Shorten(advert.Description, MaxDescriptionLength),
            Shorten(advert.Location, MaxLocationLength),
            string.Empty,
            advert.Remote,
            advert.Employment,
            SkillList.Empty,
            now);

        job.Origin = origin;
        return job;
    }

    /// <summary>Takes over what the source says now — unless the advert is closed.</summary>
    public void Mirror(ExternalAdvert advert, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(advert);

        if (State is JobState.Closed)
        {
            return;
        }

        var title = Text("The title", Shorten(advert.Title, MaxTitleLength), required: true, MaxTitleLength);
        var description = Text(
            "The description", Shorten(advert.Description, MaxDescriptionLength), required: true, MaxDescriptionLength);
        var location = Shorten(advert.Location, MaxLocationLength);
        var link = JobOrigin.Link(advert.Url);

        if (title == Title && description == Description && location == Location
            && advert.Remote == Remote && advert.Employment == Employment && link == Origin?.Url)
        {
            return;
        }

        Origin = Origin is null ? null : Origin with { Url = link };
        Title = title;
        Description = description;
        Location = location;
        Remote = advert.Remote;
        Employment = advert.Employment;
        UpdatedAt = now;
    }

    /// <summary>The source was removed: the advert becomes one of the company's own.</summary>
    public void Detach() => Origin = null;

    private static string Shorten(string? value, int limit)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..(limit - 1)].TrimEnd() + "…";
    }

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
        DateTimeOffset now,
        SalaryBand? salary = null)
    {
        // Changing a closed advert would mean changing the text people applied
        // to.
        if (State is JobState.Closed)
        {
            throw new TransitionNotAllowed(State, JobState.Draft);
        }

        // A mirrored advert takes its text from the source; what an ATS does
        // not carry — skills, salary, postal code — the company adds here, or
        // the advert never gets a tick (ADR-0052).
        if (Origin is not null
            && ((title ?? string.Empty).Trim() != Title
                || (description ?? string.Empty).Trim() != Description
                || (location ?? string.Empty).Trim() != Location
                || remote != Remote
                || employment != Employment))
        {
            throw new MirroredError();
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
        Salary = salary;
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

    // web/src/features/company/pages/CompanyJobNewPage.tsx (CREATE_CASES) matches
    // "must not be empty" and "is longer than N characters".
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

/// <summary>Where a fetched advert came from (ADR-0052).</summary>
/// <param name="SourceId">The company's source.</param>
/// <param name="ExternalId">The advert's id at the source.</param>
/// <param name="Url">Its address at the source — an https link or empty, shown and never fetched.</param>
public sealed record JobOrigin(Guid SourceId, string ExternalId, string Url)
{
    /// <summary>The longest id a source may give an advert — the column's width.</summary>
    public const int MaxIdLength = 200;

    /// <summary>The longest link kept — the column's width.</summary>
    public const int MaxUrlLength = 500;

    /// <summary>The origin of an advert, or <c>null</c> when its id cannot be kept.</summary>
    public static JobOrigin? Of(Guid sourceId, ExternalAdvert advert)
    {
        ArgumentNullException.ThrowIfNull(advert);

        return advert.ExternalId.Length is > 0 and <= MaxIdLength
            ? new JobOrigin(sourceId, advert.ExternalId, Link(advert.Url))
            : null;
    }

    /// <summary>The link, if it is an absolute https address of bearable length — otherwise nothing.</summary>
    /// <remarks>
    /// The page renders it as a link. A source decides what it says, and a
    /// <c>javascript:</c> address in a feed must not become one.
    /// </remarks>
    public static string Link(string? url) =>
        url is { Length: > 0 and <= MaxUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var address)
        && address.Scheme == Uri.UriSchemeHttps
            ? url
            : string.Empty;
}

/// <summary>An advert as a source describes it, already read into our words.</summary>
public sealed record ExternalAdvert(
    string ExternalId,
    string Title,
    string Description,
    string Location,
    RemoteMode Remote,
    EmploymentKind Employment,
    string Url);
