using Noelia.Core.Identity;

namespace WorkerTransfer.Applications.Domain.Applications;

/// <summary>Where a draft stands.</summary>
/// <remarks>
/// The way is <c>Generating → Review → (NeedsChanges ⇄ Review) → Approved →
/// Sent</c>, plus <c>Failed</c> as an exit from <c>Generating</c>.
/// <strong>There is no jump from Generating to Sent</strong>, and there must not
/// be one: between the model and the company stand two acts of a human being
/// (ADR-0034).
/// </remarks>
public enum DraftState
{
    /// <summary>The model is writing.</summary>
    Generating,

    /// <summary>Written, waiting for a human being.</summary>
    Review,

    /// <summary>There are open comments.</summary>
    NeedsChanges,

    /// <summary>A human being said: like this.</summary>
    Approved,

    /// <summary>Gone out as an application.</summary>
    Sent,

    /// <summary>The writing failed.</summary>
    Failed
}

/// <summary>What was attempted on a draft.</summary>
public enum DraftStep
{
    /// <summary>The model writes.</summary>
    Write,

    /// <summary>The person changes text or attachments.</summary>
    Change,

    /// <summary>A comment is added.</summary>
    Comment,

    /// <summary>The model revises.</summary>
    Revise,

    /// <summary>The person approves.</summary>
    Approve,

    /// <summary>The draft goes out.</summary>
    Send
}

/// <summary>This step is not possible from here.</summary>
/// <remarks>
/// <strong>The sentence stays German, and that is a contract.</strong> It
/// reaches the interface as the problem detail of <c>/write</c> and
/// <c>/revise</c>, and <c>web/src/features/work/lib/schreibfehler.ts</c>
/// recognises it by "kann nicht (geschrieben|geändert|überarbeitet) werden" —
/// an English sentence would silently turn "this step is not possible now" into
/// "the provider answers".
/// </remarks>
public sealed class DraftStepNotAllowed(DraftState now, DraftStep step)
    : InputError($"Ein Entwurf im Stand {now} kann nicht {Phrase(step)}.")
{
    private static string Phrase(DraftStep step) => step switch
    {
        DraftStep.Write => "geschrieben werden",
        DraftStep.Change => "geändert werden",
        DraftStep.Comment => "kommentiert werden",
        DraftStep.Revise => "überarbeitet werden",
        DraftStep.Approve => "freigegeben werden",
        _ => "gesendet werden"
    };
}

/// <summary>The comments do not allow this step: open ones block an approval,
/// and without an open one there is nothing to revise.</summary>
public sealed class CommentConflict(string message) : InputError(message);

/// <summary>A comment on the draft.</summary>
/// <remarks>
/// <strong>The passage is the difference between "I don't like it" and an
/// instruction.</strong> Whoever marks a passage tells the model what they are
/// talking about — and the next reader what the comment referred to, even when
/// the text has changed since.
/// </remarks>
public sealed class Comment
{
    /// <summary>How long a comment may be.</summary>
    public const int MaxTextLength = 1_000;

    /// <summary>How long the marked passage may be.</summary>
    public const int MaxPassageLength = 500;

    private Comment(
        Guid id, string text, string passage, bool resolved, DateTimeOffset createdAt)
    {
        Id = id;
        Text = text;
        Passage = passage;
        Resolved = resolved;
        CreatedAt = createdAt;
    }

    /// <summary>The id.</summary>
    public Guid Id { get; }

    /// <summary>What the human being wrote.</summary>
    public string Text { get; }

    /// <summary>The marked passage, or empty.</summary>
    public string Passage { get; }

    /// <summary>Whether a revision has already taken it up.</summary>
    public bool Resolved { get; private set; }

    /// <summary>When it came.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Writes one.</summary>
    /// <exception cref="InputError">The text is missing or too long.</exception>
    public static Comment Write(string? text, string? passage, DateTimeOffset now)
    {
        var clean = (text ?? string.Empty).Trim();

        if (clean.Length == 0)
        {
            throw new DraftInputError("A comment without text says nothing.");
        }

        if (clean.Length > MaxTextLength)
        {
            throw new DraftInputError(
                $"The comment is longer than {MaxTextLength} characters.");
        }

        var marked = (passage ?? string.Empty).Trim();

        if (marked.Length > MaxPassageLength)
        {
            marked = marked[..MaxPassageLength];
        }

        return new Comment(Guid.CreateVersion7(), clean, marked, false, now);
    }

    /// <summary>The comment as a row holds it.</summary>
    public static Comment Restore(
        Guid id, string text, string passage, bool resolved, DateTimeOffset createdAt) =>
        new(id, text, passage, resolved, createdAt);

    /// <summary>Ticks it off. Only the revision does that.</summary>
    internal void Resolve() => Resolved = true;
}

/// <summary>The input to a draft does not hold.</summary>
public sealed class DraftInputError(string message) : InputError(message);

/// <summary>A cover letter on its way to becoming an application.</summary>
/// <remarks>
/// <strong>The only place in the system where an AI result stays</strong> — and
/// ADR-0034 says why that is right here and wrong for the profile draft: this
/// is a <em>document</em> that comes about over days, in rounds, not a
/// suggestion for a form field.
/// <para>
/// What does <em>not</em> stand here is just as important: no evaluation of the
/// text, no fit value, no ranking of one's own applications by quality. That
/// would be ADR-0022 by way of one's own text.
/// </para>
/// </remarks>
public sealed class ApplicationDraft
{
    /// <summary>How long the subject line may be.</summary>
    public const int MaxSubjectLineLength = 300;

    /// <summary>How long the cover letter may be.</summary>
    public const int MaxTextLength = 20_000;

    /// <summary>How many comments a draft may carry.</summary>
    public const int MaxComments = 50;

    private readonly List<Comment> _comments;
    private readonly List<Guid> _documents;

    private ApplicationDraft(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string subjectLine,
        string text,
        DraftState state,
        int version,
        string error,
        bool sharesResume,
        List<Guid> documents,
        List<Comment> comments,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? writingStartedAt = null)
    {
        Id = id;
        Job = job;
        Company = company;
        Subject = subject;
        SubjectLine = subjectLine;
        Text = text;
        State = state;
        Version = version;
        Error = error;
        SharesResume = sharesResume;
        _documents = documents;
        _comments = comments;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        WritingStartedAt = writingStartedAt;
    }

    /// <summary>The id.</summary>
    public Guid Id { get; }

    /// <summary>For which job.</summary>
    public Guid Job { get; }

    /// <summary>Which company advertised the job.</summary>
    public TenantId Company { get; }

    /// <summary>Whose draft.</summary>
    public SubjectId Subject { get; }

    /// <summary>The subject line.</summary>
    public string SubjectLine { get; private set; }

    /// <summary>The cover letter.</summary>
    public string Text { get; private set; }

    /// <summary>Where it stands.</summary>
    public DraftState State { get; private set; }

    /// <summary>Which version this is.</summary>
    /// <remarks>
    /// It counts up with every revision. Whoever does not see that something
    /// changed does not really check (ADR-0034).
    /// </remarks>
    public int Version { get; private set; }

    /// <summary>Why it failed — the kind, never the content.</summary>
    public string Error { get; private set; }

    /// <summary>Whether the résumé goes along.</summary>
    public bool SharesResume { get; private set; }

    /// <summary>Which documents go along, in the chosen order.</summary>
    public IReadOnlyList<Guid> Documents => _documents;

    /// <summary>The comments, oldest first.</summary>
    public IReadOnlyList<Comment> Comments => _comments;

    /// <summary>Whether anything is still open.</summary>
    public bool HasOpenComments => _comments.Any(entry => !entry.Resolved);

    /// <summary>When it began.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When something last happened.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// When this writing began. Unchanged as long as the state is Generating —
    /// otherwise the interface jumps back to zero seconds with every fragment.
    /// </summary>
    public DateTimeOffset? WritingStartedAt { get; private set; }

    /// <summary>Begins a draft. The model does not write yet.</summary>
    public static ApplicationDraft Begin(
        Guid job, TenantId company, SubjectId subject, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), job, company, subject, string.Empty, string.Empty,
            DraftState.Generating, 1, string.Empty, true, [], [], now, now);

    /// <summary>The draft as rows hold it. Checks nothing.</summary>
    public static ApplicationDraft Restore(
        Guid id,
        Guid job,
        TenantId company,
        SubjectId subject,
        string subjectLine,
        string text,
        DraftState state,
        int version,
        string error,
        bool sharesResume,
        IReadOnlyList<Guid> documents,
        IReadOnlyList<Comment> comments,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? writingStartedAt = null) =>
        new(id, job, company, subject, subjectLine, text, state, version, error,
            sharesResume, [.. documents],
            [.. comments.OrderBy(entry => entry.CreatedAt)], createdAt, updatedAt,
            writingStartedAt);

    /// <summary>Starts (once more) the writing — the letter becomes empty.</summary>
    /// <remarks>
    /// From Failed back to Generating, so the interface shows "being written"
    /// and the model may fill the same draft.
    /// </remarks>
    public void StartWriting(DateTimeOffset now, bool clear = true)
    {
        if (State is not (DraftState.Generating or DraftState.Failed
            or DraftState.NeedsChanges or DraftState.Review))
        {
            throw new DraftStepNotAllowed(State, DraftStep.Write);
        }

        if (clear)
        {
            SubjectLine = string.Empty;
            Text = string.Empty;
        }

        Error = string.Empty;
        State = DraftState.Generating;
        // A writing already running keeps its start — otherwise the interface
        // jumps back to zero seconds on a second click or a poll.
        if (WritingStartedAt is null)
        {
            WritingStartedAt = now;
        }

        UpdatedAt = now;
    }

    /// <summary>An intermediate state while the model is still writing.</summary>
    /// <remarks>
    /// No new version, no jump to Review — otherwise half a letter could be
    /// approved.
    /// </remarks>
    public void TakeFragment(string? subjectLine, string? text, DateTimeOffset now)
    {
        if (State != DraftState.Generating)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Write);
        }

        SetText(subjectLine, text);
        UpdatedAt = now;
    }

    /// <summary>The model has written.</summary>
    /// <exception cref="DraftStepNotAllowed">It was not being written at all.</exception>
    public void AcceptText(string? subjectLine, string? text, DateTimeOffset now)
    {
        // From Failed too: "write once more" is the same step, only after a
        // failed attempt. Forbidding it would throw away a finished model
        // answer as a 422.
        if (State is not (DraftState.Generating or DraftState.Failed))
        {
            throw new DraftStepNotAllowed(State, DraftStep.Write);
        }

        SetText(subjectLine, text);
        State = DraftState.Review;
        Error = string.Empty;
        WritingStartedAt = null;
        UpdatedAt = now;
    }

    /// <summary>The writing failed.</summary>
    /// <remarks>
    /// <c>reason</c> names the KIND, never the content — the same rule as
    /// everywhere else: "the provider does not answer", not half the text.
    /// </remarks>
    public void Fail(string reason, DateTimeOffset now)
    {
        State = DraftState.Failed;
        Error = (reason ?? string.Empty).Trim();
        WritingStartedAt = null;
        UpdatedAt = now;
    }

    /// <summary>The person changes it herself.</summary>
    /// <remarks>
    /// Allowed in <c>Review</c> and <c>NeedsChanges</c>, not afterwards: writing
    /// on an approved text without losing the approval would mean that
    /// "approved" says nothing about the text. That is why the state falls back
    /// to <c>Review</c> here.
    /// </remarks>
    /// <exception cref="DraftStepNotAllowed">It has already gone out.</exception>
    public void EditByAuthor(string? subjectLine, string? text, DateTimeOffset now)
    {
        if (State is DraftState.Sent or DraftState.Generating)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Change);
        }

        SetText(subjectLine, text);
        State = HasOpenComments ? DraftState.NeedsChanges : DraftState.Review;
        UpdatedAt = now;
    }

    /// <summary>Chooses what goes along.</summary>
    /// <exception cref="DraftStepNotAllowed">It has already gone out.</exception>
    public void ChooseAttachments(
        bool resume, IReadOnlyList<Guid> documents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(documents);

        if (State == DraftState.Sent)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Change);
        }

        SharesResume = resume;
        _documents.Clear();
        // In the order of choice, duplicates out: the folder shows tabs, and
        // the same tab twice would be an error nobody can explain.
        _documents.AddRange(documents.Distinct());
        UpdatedAt = now;
    }

    /// <summary>Adds a comment — and with it the draft is no longer finished.</summary>
    /// <exception cref="DraftStepNotAllowed">It has already gone out.</exception>
    public Comment AddComment(string? text, string? passage, DateTimeOffset now)
    {
        if (State is DraftState.Sent or DraftState.Generating)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Comment);
        }

        if (_comments.Count >= MaxComments)
        {
            throw new DraftInputError(
                $"A draft carries no more than {MaxComments} comments.");
        }

        var comment = Comment.Write(text, passage, now);

        _comments.Add(comment);

        // BACK FROM `Approved` TOO. Whoever still finds something after the
        // approval has found it — and an approval that survives an objection is
        // none.
        State = DraftState.NeedsChanges;
        UpdatedAt = now;

        return comment;
    }

    /// <summary>What the model is to carry out.</summary>
    public IReadOnlyList<Comment> OpenComments =>
        [.. _comments.Where(entry => !entry.Resolved)];

    /// <summary>The model has revised.</summary>
    /// <remarks>
    /// <strong>Without an open comment this does not happen.</strong> A button
    /// that rewrites without an instruction would be the reflection stage from
    /// ADR-0024 under another name — two calls that change the person's words
    /// unasked.
    /// </remarks>
    /// <exception cref="CommentConflict">There is no instruction.</exception>
    public void Revise(string? subjectLine, string? text, DateTimeOffset now)
    {
        if (State == DraftState.Sent)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Revise);
        }

        if (!HasOpenComments)
        {
            throw new CommentConflict("There is no open comment — nothing to revise.");
        }

        SetText(subjectLine, text);

        foreach (var comment in _comments.Where(entry => !entry.Resolved))
        {
            comment.Resolve();
        }

        Version++;
        State = DraftState.Review;
        WritingStartedAt = null;
        UpdatedAt = now;
    }

    /// <summary>Like this, and not otherwise.</summary>
    /// <exception cref="CommentConflict">Something is still outstanding.</exception>
    public void Approve(DateTimeOffset now)
    {
        if (State is not (DraftState.Review or DraftState.NeedsChanges))
        {
            throw new DraftStepNotAllowed(State, DraftStep.Approve);
        }

        if (HasOpenComments)
        {
            throw new CommentConflict("There are open comments — revise first.");
        }

        if (Text.Length == 0)
        {
            throw new DraftInputError("Nobody approves an empty cover letter.");
        }

        State = DraftState.Approved;
        UpdatedAt = now;
    }

    /// <summary>Out.</summary>
    /// <remarks>
    /// <strong>Only from <c>Approved</c>.</strong> Approving and sending are two
    /// acts; a button "approve and send" saves a click and takes the approval's
    /// meaning away (ADR-0034).
    /// </remarks>
    /// <exception cref="DraftStepNotAllowed">It is not approved.</exception>
    public void MarkSent(DateTimeOffset now)
    {
        if (State != DraftState.Approved)
        {
            throw new DraftStepNotAllowed(State, DraftStep.Send);
        }

        State = DraftState.Sent;
        UpdatedAt = now;
    }

    private void SetText(string? subjectLine, string? text)
    {
        var line = (subjectLine ?? string.Empty).Trim();
        var body = (text ?? string.Empty).Trim();

        if (line.Length > MaxSubjectLineLength)
        {
            line = line[..MaxSubjectLineLength];
        }

        if (body.Length > MaxTextLength)
        {
            throw new DraftInputError(
                $"The cover letter is longer than {MaxTextLength} characters.");
        }

        SubjectLine = line;
        Text = body;
    }
}

/// <summary>Finds and saves drafts.</summary>
public interface IDraftStore
{
    /// <summary>This person's drafts, newest first.</summary>
    Task<IReadOnlyList<ApplicationDraft>> MineAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>One, or <c>null</c>.</summary>
    Task<ApplicationDraft?> GetAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);

    /// <summary>The open draft for this job, or <c>null</c>.</summary>
    /// <remarks>
    /// "Open" means: not sent yet. Drafting twice for the same job without
    /// noticing costs a model call and confuses the list.
    /// </remarks>
    Task<ApplicationDraft?> OpenOneAsync(
        SubjectId subject, Guid job, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(
        ApplicationDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Deletes one.</summary>
    Task DeleteAsync(
        SubjectId subject, Guid id, CancellationToken cancellationToken = default);
}
