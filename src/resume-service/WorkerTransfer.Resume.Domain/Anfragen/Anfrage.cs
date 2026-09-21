using Noelia.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Anfragen;

/// <summary>What became of one company's question.</summary>
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
public sealed class ResumeRequestRule(string code, string erklaerung) : Exception(erklaerung)
{
    /// <summary>Somebody other than the person asked tried to answer.</summary>
    public const string NichtDieBetroffene = "not_the_subject";

    /// <summary>The request already carries an answer.</summary>
    public const string AlreadyAnswered = "already_answered";

    /// <summary>The stable label of the broken rule.</summary>
    public string Code { get; } = code;
}

/// <summary>A company's request for a résumé. A proceeding, not a permission.</summary>
/// <remarks>
/// The most important statement of this type is what it does <em>not</em> have.
/// There is no <c>IstAktiv</c> and no <c>WiderrufenAm</c>: after a withdrawal
/// the request stays <see cref="Anfragestand.Granted"/> and the read still
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
        SubjectId? frager,
        RequestState state,
        DateTimeOffset gestellt,
        DateTimeOffset? beantwortet)
    {
        Id = id;
        Wer = subject;
        Firma = company;
        Frager = frager;
        State = state;
        Gestellt = gestellt;
        Beantwortet = beantwortet;
    }

    /// <summary>Which request.</summary>
    public Guid Id { get; }

    /// <summary>Whose résumé was asked for.</summary>
    public SubjectId Wer { get; }

    /// <summary>Which company asked. The permission belongs to it.</summary>
    public TenantId Firma { get; }

    /// <summary>
    /// Who inside the company asked, where that is still known.
    /// </summary>
    /// <remarks>
    /// <c>null</c> after that person deleted their own account (ADR-0027 §2):
    /// the proceeding belongs to the company and is about a <em>third</em>
    /// person whose erasure nobody asked for. It never means "nobody asked".
    /// </remarks>
    public SubjectId? Frager { get; }

    /// <summary>Where the proceeding stands.</summary>
    public RequestState State { get; private set; }

    /// <summary>When it was asked.</summary>
    public DateTimeOffset Gestellt { get; }

    /// <summary>When it was answered, if it was.</summary>
    public DateTimeOffset? Beantwortet { get; private set; }

    /// <summary>Opens one.</summary>
    public static ResumeRequest Oeffne(
        SubjectId subject,
        TenantId company,
        SubjectId frager,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, frager, RequestState.Pending, now, null);

    /// <summary>Rebuilds a stored one.</summary>
    public static ResumeRequest Wiederherstellen(
        Guid id,
        SubjectId subject,
        TenantId company,
        SubjectId? frager,
        RequestState state,
        DateTimeOffset gestellt,
        DateTimeOffset? beantwortet) =>
        new(id, subject, company, frager, state, gestellt, beantwortet);

    /// <summary>The person says yes.</summary>
    /// <exception cref="Anfrageregel">Not theirs to answer, or already answered.</exception>
    public void Erteile(SubjectId durch, DateTimeOffset now) =>
        Beantworte(durch, RequestState.Granted, now);

    /// <summary>The person says no.</summary>
    /// <exception cref="Anfrageregel">Not theirs to answer, or already answered.</exception>
    public void LehneAb(SubjectId durch, DateTimeOffset now) =>
        Beantworte(durch, RequestState.Declined, now);

    private void Beantworte(SubjectId durch, RequestState state, DateTimeOffset now)
    {
        if (durch != Wer)
        {
            throw new ResumeRequestRule(
                ResumeRequestRule.NichtDieBetroffene, "Nur die gefragte Person darf antworten.");
        }

        if (State != RequestState.Pending)
        {
            // A second "grant" after a "decline" would quietly reverse the
            // refusal. And a withdrawal belongs in the ledger, not in this
            // proceeding.
            throw new ResumeRequestRule(
                ResumeRequestRule.AlreadyAnswered, $"Diese Anfrage ist bereits {State}.");
        }

        State = state;
        Beantwortet = now;
    }
}
