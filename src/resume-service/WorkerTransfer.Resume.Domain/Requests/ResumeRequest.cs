using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Requests;

/// <summary>What became of one company's question.</summary>
/// <remarks>
/// The members are the labels of the Postgres enum <c>request_status</c>
/// (Npgsql derives them from the member names), and their upper-case form is
/// the <c>status</c> on the wire. Renaming one changes both.
/// </remarks>
public enum RequestState
{
    /// <summary>Asked, not yet answered.</summary>
    Pending,

    /// <summary>
    /// Was granted — once. Never "holds now".
    /// </summary>
    /// <remarks>
    /// Whether access holds at this moment is answered by the consent ledger
    /// alone, freshly on every read (ADR-0013).
    /// </remarks>
    Granted,

    /// <summary>Was refused. Asking again is not allowed.</summary>
    Declined
}

/// <summary>A rule of the request that the caller broke.</summary>
public sealed class ResumeRequestRule(string code, string explanation) : Exception(explanation)
{
    /// <summary>Somebody other than the person asked tried to answer.</summary>
    public const string NotTheSubject = "not_the_subject";

    /// <summary>The request already carries an answer.</summary>
    public const string AlreadyAnswered = "already_answered";

    /// <summary>The stable label of the broken rule.</summary>
    public string Code { get; } = code;
}

/// <summary>A company's request for a résumé. A proceeding, not a permission.</summary>
/// <remarks>
/// The most important statement of this type is what it does <em>not</em> have.
/// There is no <c>IsActive</c> and no <c>RevokedAt</c>: after a withdrawal
/// the request stays <see cref="RequestState.Granted"/> and the read still
/// comes up empty. That is not a contradiction but the separation between what
/// <em>happened</em> and what <em>holds</em> — and the second one only the
/// ledger knows.
/// <para>
/// Adding either field would put a second answer beside the ledger's, and the
/// two would disagree the first time somebody withdrew.
/// </para>
/// </remarks>
public sealed class ResumeRequest
{
    private ResumeRequest(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? askedBy,
        RequestState state,
        DateTimeOffset askedAt,
        DateTimeOffset? answeredAt)
    {
        Id = id;
        Subject = subject;
        Company = company;
        AskedBy = askedBy;
        State = state;
        AskedAt = askedAt;
        AnsweredAt = answeredAt;
    }

    /// <summary>Which request.</summary>
    public Guid Id { get; }

    /// <summary>Whose résumé was asked for.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which company asked. The permission belongs to it.</summary>
    public TenantId Company { get; }

    /// <summary>
    /// Who inside the company asked, where that is still known.
    /// </summary>
    /// <remarks>
    /// <c>null</c> after that person deleted their own account (ADR-0027 §2):
    /// the proceeding belongs to the company and is about a <em>third</em>
    /// person whose erasure nobody asked for. It never means "nobody asked".
    /// </remarks>
    public SubjectId? AskedBy { get; }

    /// <summary>Where the proceeding stands.</summary>
    public RequestState State { get; private set; }

    /// <summary>When it was asked.</summary>
    public DateTimeOffset AskedAt { get; }

    /// <summary>When it was answered, if it was.</summary>
    public DateTimeOffset? AnsweredAt { get; private set; }

    /// <summary>Opens one.</summary>
    public static ResumeRequest Open(
        SubjectId subject,
        TenantId company,
        SubjectId askedBy,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, askedBy, RequestState.Pending, now, null);

    /// <summary>Rebuilds a stored one.</summary>
    public static ResumeRequest Restore(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? askedBy,
        RequestState state,
        DateTimeOffset askedAt,
        DateTimeOffset? answeredAt) =>
        new(id, subject, company, askedBy, state, askedAt, answeredAt);

    /// <summary>The person says yes.</summary>
    /// <exception cref="ResumeRequestRule">Not theirs to answer, or already answered.</exception>
    public void Grant(SubjectId by, DateTimeOffset now) =>
        Answer(by, RequestState.Granted, now);

    /// <summary>The person says no.</summary>
    /// <exception cref="ResumeRequestRule">Not theirs to answer, or already answered.</exception>
    public void Decline(SubjectId by, DateTimeOffset now) =>
        Answer(by, RequestState.Declined, now);

    private void Answer(SubjectId by, RequestState state, DateTimeOffset now)
    {
        if (by != Subject)
        {
            throw new ResumeRequestRule(
                ResumeRequestRule.NotTheSubject, "only the person asked may answer");
        }

        if (State != RequestState.Pending)
        {
            // A second "grant" after a "decline" would quietly reverse the
            // refusal. And a withdrawal belongs in the ledger, not in this
            // proceeding.
            throw new ResumeRequestRule(
                ResumeRequestRule.AlreadyAnswered, $"this request is already {State}");
        }

        State = state;
        AnsweredAt = now;
    }
}
