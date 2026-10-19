using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Applications;

/// <summary>Where an application stands.</summary>
public enum ApplicationState
{
    /// <summary>Sent.</summary>
    Submitted,

    /// <summary>The company is looking at it.</summary>
    Reviewing,

    /// <summary>Turned down.</summary>
    Rejected,

    /// <summary>Withdrawn by the person.</summary>
    Withdrawn,

    /// <summary>Hired.</summary>
    Hired
}

/// <summary>Something about the input is wrong.</summary>
public abstract class InputError(string message) : Exception(message);

/// <summary>The message is too long.</summary>
public sealed class MessageTooLong(int limit)
    // web/src/features/work/lib/draftErrors.ts matches this sentence.
    : InputError($"The message is longer than {limit} characters.");

/// <summary>This is another person's application.</summary>
public sealed class NotYours() : InputError("This application belongs to someone else.");

/// <summary>This step is not possible from here.</summary>
public sealed class TransitionNotAllowed(ApplicationState now, ApplicationState wanted)
    // web/src/features/work/lib/draftErrors.ts matches this sentence.
    : InputError($"A {now} application cannot become {wanted}.");

/// <summary>What is sent along.</summary>
/// <remarks>
/// The profile always goes along and cannot be deselected: an application
/// without any statement about the person is none, and "I am applying, but you
/// may see nothing of me" is not a choice anybody seriously makes.
/// </remarks>
public sealed record Enclosures(
    bool CurriculumVitae = false,
    bool Portfolio = false,
    IReadOnlyList<Guid>? Documents = null)
{
    /// <summary>Always true. Not a field, so nobody sets it to false.</summary>
    public bool Profile => true;

    /// <summary>Which documents are enclosed — the ids, in their order.</summary>
    /// <remarks>
    /// <strong>A snapshot, not a reference to "everything she has".</strong>
    /// Whoever uploads a document later has not enclosed it with this
    /// application; whoever deletes one has sent it all the same. The list here
    /// says what actually went out.
    /// </remarks>
    public IReadOnlyList<Guid> Documents { get; init; } = Documents ?? [];
}

/// <summary>An application.</summary>
/// <remarks>
/// It belongs to <em>both</em> — the person and the company. That is why each
/// side has its own ways: the person withdraws, the company moves it through the
/// process, and neither of the two may do what the other may.
/// <para>
/// There is no score and no ranking here. A list of applications sorted by
/// anything this service computed is candidate rating through the back door
/// (ADR-0022).
/// </para>
/// </remarks>
public sealed class JobApplication
{
    /// <summary>How long the message may be.</summary>
    public const int MaxMessageLength = 4000;

    /// <summary>
    /// States from which there is no way back into a running process.
    /// </summary>
    /// <remarks>
    /// Sending again after a rejection would be chasing a "no" that has already
    /// been said — the same rule as for the résumé.
    /// </remarks>
    private static readonly ApplicationState[] Final =
        [ApplicationState.Rejected, ApplicationState.Hired];

    private JobApplication(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Enclosures enclosures,
        ApplicationState state,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? answeredAt,
        ApplicantContact contact)
    {
        Id = id;
        Job = job;
        Company = company;
        Subject = subject;
        Message = message;
        Enclosures = enclosures;
        State = state;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        AnsweredAt = answeredAt;
        Contact = contact;
    }

    /// <summary>Which application.</summary>
    public Guid Id { get; }

    /// <summary>For which job.</summary>
    public Guid Job { get; }

    /// <summary>
    /// Which company — copied from the job, on purpose.
    /// </summary>
    /// <remarks>
    /// A foreign key is impossible (another database, ADR-0004), and a call per
    /// read would be expensive for a value that never changes: a job does not
    /// change its company. A copy is only dangerous when the original can change.
    /// </remarks>
    public TenantId Company { get; }

    /// <summary>Who applied.</summary>
    public SubjectId Subject { get; }

    /// <summary>What the person wrote.</summary>
    public string Message { get; private set; }

    /// <summary>What she sent along.</summary>
    public Enclosures Enclosures { get; private set; }

    /// <summary>Where it stands.</summary>
    public ApplicationState State { get; private set; }

    /// <summary>When it was sent.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When something last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary><c>null</c> as long as nothing is decided.</summary>
    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>
    /// The letterhead at the moment of sending — civil name, postal address, phone, mail.
    /// </summary>
    /// <remarks>
    /// A snapshot, not a reference (ADR-0038): a later move must not rewrite the
    /// company's folder. Never in the <c>CoverLetterContext</c>, never sent to a model.
    /// </remarks>
    public ApplicantContact Contact { get; private set; }

    /// <summary>
    /// Is it still running — and with it the release of the data?
    /// </summary>
    /// <remarks>
    /// Why this is a question to the application and not to a visibility
    /// column: as long as it runs, the company may see what was sent along.
    /// Afterwards not any more, and nobody has to remember to take something
    /// back.
    /// </remarks>
    public bool InProgress => State is ApplicationState.Submitted or ApplicationState.Reviewing;

    /// <summary>Sends an application.</summary>
    public static JobApplication Submit(
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Enclosures enclosures,
        DateTimeOffset now,
        ApplicantContact? contact = null) =>
        new(Guid.CreateVersion7(), job, company, subject, CleanMessage(message), enclosures,
            ApplicationState.Submitted, now, now, null,
            contact ?? ApplicantContact.Empty);

    /// <summary>The application as a row holds it.</summary>
    public static JobApplication Restore(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string message,
        Enclosures enclosures,
        ApplicationState state,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? answeredAt,
        ApplicantContact? contact = null) =>
        new(id, job, company, subject, message, enclosures, state,
            createdAt, updatedAt, answeredAt, contact ?? ApplicantContact.Empty);

    /// <summary>Applying again after a withdrawal — a new decision.</summary>
    /// <remarks>
    /// Not after a rejection: that would be chasing a "no" that has already been
    /// said.
    /// </remarks>
    /// <exception cref="TransitionNotAllowed">It was not withdrawn.</exception>
    public void Resubmit(
        string message,
        Enclosures enclosures,
        DateTimeOffset now,
        ApplicantContact? contact = null)
    {
        if (State is not ApplicationState.Withdrawn)
        {
            throw new TransitionNotAllowed(State, ApplicationState.Submitted);
        }

        Message = CleanMessage(message);
        Enclosures = enclosures;
        Contact = contact ?? ApplicantContact.Empty;
        State = ApplicationState.Submitted;
        AnsweredAt = null;
        UpdatedAt = now;
    }

    /// <summary>Withdraws it.</summary>
    /// <remarks>
    /// Always possible while it runs — also from <c>Reviewing</c>: whoever no
    /// longer wants to need not wait until somebody else is done.
    /// </remarks>
    /// <exception cref="NotYours">It belongs to someone else.</exception>
    /// <exception cref="TransitionNotAllowed">It no longer runs.</exception>
    public void Withdraw(SubjectId by, DateTimeOffset now)
    {
        if (by != Subject)
        {
            throw new NotYours();
        }

        if (!InProgress)
        {
            throw new TransitionNotAllowed(State, ApplicationState.Withdrawn);
        }

        State = ApplicationState.Withdrawn;
        UpdatedAt = now;
    }

    /// <summary>The company moves it through the process.</summary>
    /// <exception cref="TransitionNotAllowed">That is not possible from here.</exception>
    public void Move(ApplicationState target, DateTimeOffset now)
    {
        if (target is not (ApplicationState.Reviewing or ApplicationState.Rejected
                         or ApplicationState.Hired))
        {
            // A company does not withdraw an application — that is the
            // person's act, and allowing it here would mean being able to do it
            // in her name.
            throw new TransitionNotAllowed(State, target);
        }

        // A withdrawn application is none any more; a rejected or accepted one
        // is decided.
        if (!InProgress)
        {
            throw new TransitionNotAllowed(State, target);
        }

        State = target;

        if (Final.Contains(target))
        {
            AnsweredAt = now;
        }

        UpdatedAt = now;
    }

    private static string CleanMessage(string? message)
    {
        var cleaned = (message ?? string.Empty).Trim();

        return cleaned.Length > MaxMessageLength
            ? throw new MessageTooLong(MaxMessageLength)
            : cleaned;
    }
}

/// <summary>
/// The letterhead at the moment of sending. Belongs to the folder, not to the prompt (ADR-0038).
/// </summary>
public sealed record ApplicantContact(
    string FullName,
    string Line1,
    string Line2,
    string PostalCode,
    string City,
    string Country,
    string Phone,
    string Email)
{
    /// <summary>Nothing on file — the letterhead stays empty, the sending does not fail.</summary>
    public static ApplicantContact Empty { get; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, "DE", string.Empty, string.Empty);
}

/// <summary>Finds and saves applications.</summary>
public interface IApplicationStore
{
    /// <summary>An application, or <c>null</c>.</summary>
    Task<JobApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>This person's application for this job, if there is one.</summary>
    /// <remarks>
    /// A person applies once per job. A second time is either the same
    /// application sent again or not allowed at all — never a second row.
    /// </remarks>
    Task<JobApplication?> GetAsync(
        Guid job, SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(JobApplication application, CancellationToken cancellationToken = default);

    /// <summary>A person's applications, newest first.</summary>
    Task<IReadOnlyList<JobApplication>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>The applications for a job of this company.</summary>
    Task<IReadOnlyList<JobApplication>> ForJobAsync(
        Guid job, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>How many applications this company has in which state.</summary>
    Task<IReadOnlyDictionary<ApplicationState, int>> CountAsync(
        TenantId company, CancellationToken cancellationToken = default);
}

/// <summary>The words with which a state stands on the wire and in the column.</summary>
/// <remarks>
/// In one place, because two edges need the same word — the column
/// <c>applications.status</c> and the field <c>status</c> in the contract. Two
/// translations would be two chances to differ, and the interface checks the
/// words one by one.
/// </remarks>
public static class ApplicationStates
{
    /// <summary>The word for the state.</summary>
    public static string Word(ApplicationState state) => state switch
    {
        ApplicationState.Submitted => "submitted",
        ApplicationState.Reviewing => "reviewing",
        ApplicationState.Rejected => "rejected",
        ApplicationState.Withdrawn => "withdrawn",
        ApplicationState.Hired => "hired",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The state for the word, or <c>null</c>.</summary>
    public static ApplicationState? Parse(string? word) => word switch
    {
        "submitted" => ApplicationState.Submitted,
        "reviewing" => ApplicationState.Reviewing,
        "rejected" => ApplicationState.Rejected,
        "withdrawn" => ApplicationState.Withdrawn,
        "hired" => ApplicationState.Hired,
        _ => null
    };
}
