using Noelia.Core.Identity;

namespace WorkerTransfer.Assessment.Domain.Cases;

/// <summary>Something about the input is wrong.</summary>
/// <remarks>
/// The message names the <em>rule</em> and never the value. The text of an
/// assignment or of an evaluation would otherwise end up in the log — and both
/// are about a human being.
/// </remarks>
/// <param name="message">What the rule demands.</param>
public sealed class InputError(string message) : Exception(message);

/// <summary>This step is not possible from here.</summary>
/// <remarks>
/// Separate from <see cref="InputError"/>, because it is something else: the
/// input was fine, the case simply stands elsewhere. The endpoint turns it into
/// 409 and not 422 — "that does not work now" is no statement about the form.
/// </remarks>
/// <param name="message">Why not.</param>
public sealed class StepNotPossible(string message) : Exception(message);

/// <summary>There is nothing to see here.</summary>
/// <remarks>
/// <para><strong>One exit for four situations:</strong> the case does not
/// exist; it belongs to somebody else; it belongs to another company; the
/// person has released nothing (any more) to this company. All four answer 404,
/// byte-identical apart from the correlation id (ADR-0020 §1).</para>
///
/// <para>A code of its own for the last would be the "locked" hint — and would
/// give away exactly what is not released: that there is something.</para>
/// </remarks>
public sealed class NoCase() : Exception("No such assessment");

/// <summary>Where a case stands.</summary>
/// <remarks>
/// <para><strong>Four values, and none of them is called "declined".</strong>
/// Whoever does not want to accept an assignment does nothing; the deadline
/// passes, and the case stands at <see cref="Expired"/>. That state is
/// indistinguishable from "meant to and didn't" and from "was ill" — it says
/// only that nothing was submitted (ADR-0042 §3).</para>
///
/// <para><strong>It is not stored but computed</strong> — from the submission,
/// the evaluation and the clock. A column would have to be switched by somebody
/// when a deadline passes, and that would be a nightly run touching cases about
/// human beings without anybody having asked.</para>
/// </remarks>
public enum State
{
    /// <summary>Set, nothing submitted, the deadline is running.</summary>
    Set,

    /// <summary>Submitted, not evaluated yet.</summary>
    Submitted,

    /// <summary>Evaluated — and the person reads the evaluation.</summary>
    Evaluated,

    /// <summary>The deadline has passed without anything arriving.</summary>
    Expired
}

/// <summary>How the company answers the work.</summary>
/// <remarks>
/// <para>Two values, and expressly <strong>no number</strong>: no mark, no
/// stars, no percentage, no "3 out of 5" (ADR-0042 §1). A number hides exactly
/// what helps — what was missing from the result — and it looks like a
/// measurement while doing so.</para>
///
/// <para>It is about the <em>case</em> and not about the human being: "we are
/// going on" or "we are not going on". There is no place where such outcomes
/// are counted across cases, and no endpoint that gives them out across company
/// boundaries.</para>
/// </remarks>
public enum Outcome
{
    /// <summary>It goes on.</summary>
    Accepted,

    /// <summary>It does not go on — and the reason stands beside it.</summary>
    Rejected
}

/// <summary>The words state and outcome travel under on the wire.</summary>
public static class Words
{
    /// <summary>The word for a state.</summary>
    public static string Word(State state) => state switch
    {
        State.Set => "set",
        State.Submitted => "submitted",
        State.Evaluated => "evaluated",
        State.Expired => "expired",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The word for an outcome.</summary>
    public static string Word(Outcome outcome) => outcome switch
    {
        Outcome.Accepted => "accepted",
        Outcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    /// <summary>The outcome for a word, or <c>null</c>.</summary>
    /// <remarks>
    /// <c>ReadOutcome</c> and not <c>Outcome</c>: a method named like its return
    /// type hides that type across the whole namespace.
    /// </remarks>
    public static Outcome? ReadOutcome(string? word) => word switch
    {
        "accepted" => Outcome.Accepted,
        "rejected" => Outcome.Rejected,
        _ => null
    };
}

/// <summary>The assignment itself — and the two numbers that bound it.</summary>
/// <remarks>
/// <para><strong><see cref="Hours"/> is required, and that is this service's
/// whole point.</strong> A work sample without a stated size is an assignment
/// whose price the person learns only once they have paid it. The size stands
/// up front so they can decide.</para>
///
/// <para>A number is therefore expressly allowed here — it is about the
/// <em>assignment</em>. What is forbidden are numbers about the human being,
/// and the difference is the whole dividing line of ADR-0022: requirement in,
/// evidence out; never human in, number out.</para>
/// </remarks>
public sealed record Assignment
{
    /// <summary>The upper bound of the size, in hours.</summary>
    /// <remarks>
    /// Eight, and that is a decision: more than one working day is no longer a
    /// sample but work — and for work there is a contract and not a form. The
    /// platform simply offers no field for "sixteen hours". Whoever wants that
    /// has to do it outside, and then it is visibly what it is (ADR-0042 §3).
    /// </remarks>
    public const int MaxHours = 8;

    /// <summary>And the lower bound.</summary>
    /// <remarks>
    /// One hour. Not zero: "it costs you nothing" is no statement but a
    /// reassurance, and it would be the first thing somebody entered.
    /// </remarks>
    public const int MinHours = 1;

    /// <summary>How far away the deadline lies at least.</summary>
    /// <remarks>
    /// Forty-eight hours. An assignment for tomorrow morning does not measure
    /// how somebody works but whether they can drop everything else right now —
    /// and that has nothing to do with ability and everything to do with living
    /// circumstances. It is the same skew ADR-0022 describes at GitHub.
    /// </remarks>
    public static readonly TimeSpan MinNotice = TimeSpan.FromHours(48);

    /// <summary>How long the title may be.</summary>
    public const int MaxTitleLength = 200;

    /// <summary>How long the assignment text may be.</summary>
    public const int MaxTextLength = 8000;

    private Assignment(string title, string text, int hours, DateTimeOffset deadline)
    {
        Title = title;
        Text = text;
        Hours = hours;
        Deadline = deadline;
    }

    /// <summary>The heading.</summary>
    public string Title { get; }

    /// <summary>What is to be done.</summary>
    public string Text { get; }

    /// <summary>The stated size in hours, 1 to 8.</summary>
    public int Hours { get; }

    /// <summary>By when.</summary>
    public DateTimeOffset Deadline { get; }

    /// <summary>Checks every value and only then builds.</summary>
    /// <exception cref="InputError">A value does not keep the rule.</exception>
    public static Assignment Write(
        string? title, string? text, int hours, DateTimeOffset deadline, DateTimeOffset now)
    {
        var checkedTitle = RequiredText(title, MaxTitleLength, "a title");
        var checkedText = RequiredText(text, MaxTextLength, "a task description");

        if (hours is < MinHours or > MaxHours)
        {
            throw new InputError(
                $"the stated effort is between {MinHours} and {MaxHours} hours");
        }

        if (deadline - now < MinNotice)
        {
            throw new InputError(
                $"a deadline lies at least {MinNotice.TotalHours:0} hours ahead");
        }

        return new Assignment(checkedTitle, checkedText, hours, deadline);
    }

    /// <summary>The assignment as a row holds it — without checking again.</summary>
    /// <remarks>
    /// A stored row was valid when it came into being. Refusing it on reading
    /// would mean taking somebody's case away because a limit has changed.
    /// </remarks>
    public static Assignment Restore(
        string title, string text, int hours, DateTimeOffset deadline) =>
        new(title, text, hours, deadline);

    private static string RequiredText(string? raw, int limit, string what)
    {
        var value = (raw ?? string.Empty).Trim();

        return value.Length switch
        {
            0 => throw new InputError($"{what} is required"),
            var length when length > limit =>
                throw new InputError($"{what} is at most {limit} characters"),
            _ => value
        };
    }
}

/// <summary>What the person handed in.</summary>
/// <remarks>
/// <para><strong>Text and at most one address — never bytes.</strong> A
/// person's files lie in portfolio-service and in resume-service, and both clear
/// them away in the cascade. A third store would be a third place an erasure has
/// to reach — and the one it one day does not reach (ADR-0042).</para>
///
/// <para><strong>The address is never fetched.</strong> It is stored and shown,
/// and a human being clicks it. A service that fetched it would carry an address
/// chosen by a stranger into a server request — and the egress boundary would
/// refuse it anyway, because it stands in no configuration. That is no obstacle
/// here but the right answer.</para>
/// </remarks>
public sealed record Submission
{
    /// <summary>How long the accompanying text may be.</summary>
    public const int MaxTextLength = 8000;

    /// <summary>How long an address may be.</summary>
    public const int MaxUrlLength = 2000;

    private Submission(string text, string? url, DateTimeOffset at)
    {
        Text = text;
        Url = url;
        At = at;
    }

    /// <summary>What the person wrote about it. May be empty.</summary>
    public string Text { get; }

    /// <summary>Where it points, or <c>null</c>.</summary>
    public string? Url { get; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Checks and builds.</summary>
    /// <remarks>
    /// One of the two has to be there. An empty submission would be none at all
    /// — and it would stand there as "submitted", which is the wrong promise to
    /// the person and the wrong information to the company.
    /// </remarks>
    /// <exception cref="InputError">A value does not keep the rule.</exception>
    public static Submission Write(string? text, string? url, DateTimeOffset now)
    {
        var accompanying = (text ?? string.Empty).Trim();
        var target = CheckedUrl((url ?? string.Empty).Trim());

        if (accompanying.Length == 0 && target is null)
        {
            throw new InputError("a submission carries a text or a link");
        }

        if (accompanying.Length > MaxTextLength)
        {
            throw new InputError($"a submission text is at most {MaxTextLength} characters");
        }

        return new Submission(accompanying, target, now);
    }

    /// <summary>The submission as a row holds it.</summary>
    public static Submission Restore(string text, string? url, DateTimeOffset at) =>
        new(text, url, at);

    private static string? CheckedUrl(string raw)
    {
        if (raw.Length == 0)
        {
            return null;
        }

        if (raw.Length > MaxUrlLength)
        {
            throw new InputError($"a link is at most {MaxUrlLength} characters");
        }

        // Only http and https, and only as an absolute address. `javascript:` and
        // `data:` would otherwise stand as a link in somebody else's interface —
        // stored by one human being, clicked by another.
        return Uri.TryCreate(raw, UriKind.Absolute, out var address)
               && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
            ? address.ToString()
            : throw new InputError("a link is an absolute http or https address");
    }
}

/// <summary>The company's reply.</summary>
/// <remarks>
/// <para><strong>One field, and the person reads exactly that one.</strong>
/// There is no internal field beside it, no note, no "just for us". Where there
/// were two, the truth would stand in the second (ADR-0042 §2).</para>
///
/// <para><strong>The text is required, on a rejection too.</strong> Rejecting
/// and giving a reason are one step and not two — the second could otherwise be
/// left out, and that is exactly the trade this service must not broker: work
/// for silence.</para>
/// </remarks>
public sealed record Evaluation
{
    /// <summary>How long the reply may be.</summary>
    public const int MaxTextLength = 8000;

    private Evaluation(string text, Outcome outcome, DateTimeOffset at)
    {
        Text = text;
        Outcome = outcome;
        At = at;
    }

    /// <summary>What the company wrote. Never empty.</summary>
    public string Text { get; }

    /// <summary>Whether it goes on.</summary>
    public Outcome Outcome { get; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; }

    /// <summary>Checks and builds.</summary>
    /// <exception cref="InputError">The text is missing or too long.</exception>
    public static Evaluation Write(string? text, Outcome outcome, DateTimeOffset now)
    {
        var word = (text ?? string.Empty).Trim();

        return word.Length switch
        {
            0 => throw new InputError("an evaluation carries a text, a rejection too"),
            var length when length > MaxTextLength =>
                throw new InputError($"an evaluation is at most {MaxTextLength} characters"),
            _ => new Evaluation(word, outcome, now)
        };
    }

    /// <summary>The evaluation as a row holds it.</summary>
    public static Evaluation Restore(string text, Outcome outcome, DateTimeOffset at) =>
        new(text, outcome, at);
}

/// <summary>One case: one assignment, one submission, one evaluation.</summary>
/// <remarks>
/// <para><strong>The evaluation belongs to this aggregate and to nothing
/// else</strong> (ADR-0042 §1). It stands in no search, in no profile, and no
/// second company sees it — not because a check wards that off but because
/// there is no way there: the store knows two questions, "this company's cases"
/// and "my cases", and a third ("this person's cases", asked by a company) does
/// not exist.</para>
///
/// <para><strong>It is evaluated once.</strong> A second time is 409. An
/// evaluation that can be changed afterwards is one the person may have read
/// before it got its final wording.</para>
/// </remarks>
public sealed class Case
{
    private Case(
        Guid id,
        SubjectId subject,
        TenantId company,
        Assignment assignment,
        Submission? submission,
        Evaluation? evaluation,
        DateTimeOffset setAt,
        DateTimeOffset changedAt)
    {
        Id = id;
        Subject = subject;
        Company = company;
        Assignment = assignment;
        Submission = submission;
        Evaluation = evaluation;
        SetAt = setAt;
        ChangedAt = changedAt;
    }

    /// <summary>Which case.</summary>
    public Guid Id { get; }

    /// <summary>Whom the assignment was set for.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which company set it.</summary>
    public TenantId Company { get; }

    /// <summary>What was to be done.</summary>
    public Assignment Assignment { get; }

    /// <summary>What was handed in, or <c>null</c>.</summary>
    public Submission? Submission { get; private set; }

    /// <summary>What came back, or <c>null</c>.</summary>
    public Evaluation? Evaluation { get; private set; }

    /// <summary>When the assignment was set.</summary>
    public DateTimeOffset SetAt { get; }

    /// <summary>When something last moved.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Where the case stands at this moment.</summary>
    /// <remarks>
    /// Computed, not stored. The order of the questions is the statement: an
    /// expired deadline does not undo a submitted case — whoever delivered has
    /// delivered, and the company still owes the answer afterwards.
    /// </remarks>
    public State StateAt(DateTimeOffset now)
    {
        if (Evaluation is not null)
        {
            return State.Evaluated;
        }

        if (Submission is not null)
        {
            return State.Submitted;
        }

        return now > Assignment.Deadline ? State.Expired : State.Set;
    }

    /// <summary>Sets an assignment.</summary>
    public static Case Open(
        SubjectId subject, TenantId company, Assignment assignment, DateTimeOffset now) =>
        new(Guid.NewGuid(), subject, company, assignment, null, null, now, now);

    /// <summary>The case as a row holds it.</summary>
    public static Case Restore(
        Guid id,
        SubjectId subject,
        TenantId company,
        Assignment assignment,
        Submission? submission,
        Evaluation? evaluation,
        DateTimeOffset setAt,
        DateTimeOffset changedAt) =>
        new(id, subject, company, assignment, submission, evaluation, setAt, changedAt);

    /// <summary>The person submits.</summary>
    /// <remarks>
    /// After the deadline it is no longer possible, and that protects both
    /// sides: an assignment without an end is one that stays open in somebody's
    /// head for ever.
    /// </remarks>
    /// <exception cref="StepNotPossible">Already submitted, or past the deadline.</exception>
    public void Submit(Submission submission, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (Submission is not null)
        {
            throw new StepNotPossible("this assessment has already been submitted");
        }

        if (now > Assignment.Deadline)
        {
            throw new StepNotPossible("the deadline for this assessment has passed");
        }

        Submission = submission;
        ChangedAt = now;
    }

    /// <summary>The company answers.</summary>
    /// <remarks>
    /// Without a submission there is nothing to evaluate — and an evaluation
    /// without work would be a statement about the human being instead of about
    /// their work.
    /// </remarks>
    /// <exception cref="StepNotPossible">Nothing submitted, or already evaluated.</exception>
    public void Evaluate(Evaluation evaluation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        if (Submission is null)
        {
            throw new StepNotPossible("there is nothing submitted to evaluate");
        }

        if (Evaluation is not null)
        {
            throw new StepNotPossible("this assessment has already been evaluated");
        }

        Evaluation = evaluation;
        ChangedAt = now;
    }
}

/// <summary>Finds and stores cases.</summary>
/// <remarks>
/// <para><strong>Two questions, and no third.</strong>
/// <see cref="ForCompanyAsync"/> carries the tenant from the verified token;
/// <see cref="ForPersonAsync"/> is a human being's own list. A method "this
/// person's cases", callable by a company, would be the agency from ADR-0042 §1
/// — and it does not exist here, so nobody finds it.</para>
/// </remarks>
public interface ICaseStore
{
    /// <summary>One case, or <c>null</c>.</summary>
    Task<Case?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Everything set for a person — their own list.</summary>
    Task<IReadOnlyList<Case>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Everything a company has set.</summary>
    Task<IReadOnlyList<Case>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Case caseItem, CancellationToken cancellationToken = default);

    /// <summary>Erases everything about a person.</summary>
    /// <returns>How much deliberately stayed. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
