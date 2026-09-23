using Noelia.Core.Identity;
using MediatR;
using WorkerTransfer.Assessment.Application.Messages;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Assessment.Application.Cases;

/// <summary>The words this service writes into the outbox.</summary>
/// <remarks>
/// <para><strong>One kind for both movements</strong> — the same choice as with
/// <c>application_update</c> and <c>transfer_update</c>. A notification carries
/// only a kind and no content anyway; two kinds would be two switches in the
/// settings for the same thing.</para>
///
/// <para><strong>And it is the reason the evaluation arrives.</strong> Without
/// this note the person would learn of a reply only at their next visit — and a
/// judgement the judged does not read is exactly what is not built here
/// (ADR-0042 §2).</para>
/// </remarks>
public static class NotificationKinds
{
    /// <summary>The word on the wire. The same one notification-service reads.</summary>
    public const string Moved = "assessment_update";
}

// ---------------------------------------------------------------------------
// Setting

/// <summary>A company sets an assignment.</summary>
public sealed record SetTaskCommand(
    SubjectId Subject,
    TenantId Company,
    string? Title,
    string? Text,
    int Hours,
    DateTimeOffset Deadline) : ICommandMessage<Case>;

/// <summary>Creates the case — but only if the person has released.</summary>
/// <remarks>
/// <para><strong>The order is the promise.</strong> The ledger first: whoever
/// has released nothing to this company has nothing here — and gets the same
/// 404 as a human being who does not exist. Only afterwards is the body
/// checked. The other way round a stranger would learn from the difference
/// between 422 and 404 that their values were fine — and with it that this
/// human being exists.</para>
///
/// <para><strong>No capability of its own, and no coupling to
/// advisor-service.</strong> In practice an assignment follows a conversation,
/// but it does not <em>hang</em> on one: a call from here to there would make
/// one service out of two and make the answer to "may I?" depend on a third
/// one's availability. Both ask the ledger, and the ledger is the one place
/// (ADR-0042).</para>
///
/// <para><strong>The person learns of it</strong> — over the content-free
/// outbox and in the same transaction as the case. The message names no company
/// and no assignment text: it carries only a kind.</para>
/// </remarks>
public sealed class SetTaskHandler(
    ICaseStore cases,
    IConsentGate gate,
    IOutbox outbox,
    TimeProvider clock) : IRequestHandler<SetTaskCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="NoCase">This person has released nothing.</exception>
    /// <exception cref="InputError">A value does not keep the rule.</exception>
    public async Task<Case> Handle(
        SetTaskCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await gate.MaySeeAsync(request.Subject, request.Company, cancellationToken))
        {
            throw new NoCase();
        }

        var now = clock.GetUtcNow();

        var assignment = Assignment.Write(
            request.Title, request.Text, request.Hours, request.Deadline, now);

        var caseItem = Case.Open(request.Subject, request.Company, assignment, now);

        await cases.SaveAsync(caseItem, cancellationToken);
        await outbox.NoteAsync(
            request.Subject, NotificationKinds.Moved, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Submitting

/// <summary>The person submits their solution.</summary>
public sealed record SubmitCommand(
    Guid Id, SubjectId Subject, string? Text, string? Url) : ICommandMessage<Case>;

/// <summary>Accepts the solution — and does not ask the ledger while doing so.</summary>
/// <remarks>
/// <para><strong>The person's side is never guarded by the ledger</strong>
/// (ADR-0042 §2). Whoever wants to answer an assignment set for them needs no
/// release for it — the release governs what the company <em>sees</em>, not what
/// the person may <em>do</em>. The reverse build would demand that a human being
/// stay visible in order to be able to hand in their own work.</para>
///
/// <para>Somebody else's case answers with the same 404 as a case that does not
/// exist: a 403 would say "it exists, it just is not yours".</para>
///
/// <para><strong>No note in the outbox, and that is no forgetting.</strong>
/// Human beings are notified; a company has no inbox. It sees the submission in
/// its own list, behind the sign-in — where who reads is checked. The two notes
/// this service writes both go to the person: "a task was set for you" and
/// "your evaluation is in".</para>
/// </remarks>
public sealed class SubmitHandler(ICaseStore cases, TimeProvider clock)
    : IRequestHandler<SubmitCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="NoCase">Does not exist, or belongs to somebody else.</exception>
    /// <exception cref="StepNotPossible">Already submitted, or past the deadline.</exception>
    public async Task<Case> Handle(
        SubmitCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Subject != request.Subject)
        {
            throw new NoCase();
        }

        var now = clock.GetUtcNow();

        caseItem.Submit(
            Submission.Write(request.Text, request.Url, now), now);

        await cases.SaveAsync(caseItem, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Evaluating

/// <summary>The company writes its reply.</summary>
public sealed record EvaluateCommand(
    Guid Id, TenantId Company, Outcome Outcome, string? Text) : ICommandMessage<Case>;

/// <summary>
/// Writes the one evaluation — and in the same breath notes that the person is
/// to read it.
/// </summary>
/// <remarks>
/// <para><strong>This is the line ADR-0042 §2 hangs on.</strong> The evaluation
/// and the note come into being in the same transaction: an evaluation without
/// the note would be a judgement the person never learns of, and the note
/// without the evaluation a message about nothing.</para>
///
/// <para><strong>The text is required — on a rejection too.</strong> That stands
/// in the aggregate and not here, so no handler can get around it.</para>
/// </remarks>
public sealed class EvaluateHandler(
    ICaseStore cases, IConsentGate gate, IOutbox outbox, TimeProvider clock)
    : IRequestHandler<EvaluateCommand, Case>
{
    /// <inheritdoc />
    /// <exception cref="NoCase">Does not exist, belongs to others, or nothing released.</exception>
    /// <exception cref="StepNotPossible">Nothing submitted, or already evaluated.</exception>
    /// <exception cref="InputError">The text is missing.</exception>
    public async Task<Case> Handle(
        EvaluateCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null || caseItem.Company != request.Company)
        {
            throw new NoCase();
        }

        if (!await gate.MaySeeAsync(caseItem.Subject, request.Company, cancellationToken))
        {
            throw new NoCase();
        }

        var now = clock.GetUtcNow();

        caseItem.Evaluate(Evaluation.Write(request.Text, request.Outcome, now), now);

        await cases.SaveAsync(caseItem, cancellationToken);
        await outbox.NoteAsync(
            caseItem.Subject, NotificationKinds.Moved, cancellationToken);

        return caseItem;
    }
}

// ---------------------------------------------------------------------------
// Reading

/// <summary>"Show me this case."</summary>
/// <remarks>
/// One query for <em>both</em> sides, and the promise hangs on that: there is
/// no company view beside a person view, because there is only one query.
/// </remarks>
/// <param name="Id">Which case.</param>
/// <param name="Subject">Who is asking — from the verified token.</param>
/// <param name="Company">In whose name, or <c>null</c> for "as a person".</param>
public sealed record CaseQuery(Guid Id, SubjectId Subject, TenantId? Company)
    : IQueryMessage<Case>;

/// <summary>Answers it without the ledger for the person and with it for the company.</summary>
/// <remarks>
/// <para><strong>The person's side wins.</strong> Whoever opens their own case
/// gets it — while acting for a company too, and after having revoked every
/// visibility too. That is exactly "the person sees the evaluation, always"
/// (ADR-0042 §2).</para>
///
/// <para><strong>For the company the ledger stands in front</strong>, at every
/// read (ADR-0013). Whoever revokes disappears for the company — 404, as
/// everywhere.</para>
/// </remarks>
public sealed class CaseHandler(ICaseStore cases, IConsentGate gate)
    : IRequestHandler<CaseQuery, Case>
{
    /// <inheritdoc />
    /// <exception cref="NoCase">Four situations, one answer.</exception>
    public async Task<Case> Handle(CaseQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caseItem = await cases.GetAsync(request.Id, cancellationToken);

        if (caseItem is null)
        {
            throw new NoCase();
        }

        if (caseItem.Subject == request.Subject)
        {
            return caseItem;
        }

        if (request.Company is not { } company || caseItem.Company != company)
        {
            throw new NoCase();
        }

        return await gate.MaySeeAsync(caseItem.Subject, company, cancellationToken)
            ? caseItem
            : throw new NoCase();
    }
}

/// <summary>"Show me my cases."</summary>
public sealed record MyCasesQuery(SubjectId Subject) : IQueryMessage<IReadOnlyList<Case>>;

/// <summary>One's own list — unfiltered, and without asking the ledger.</summary>
/// <remarks>
/// Checking one's own consent in order to see one's own work would not just be
/// a superfluous hop: whoever has revoked would otherwise no longer get at their
/// evaluations (ADR-0020 §5, ADR-0042 §2).
/// </remarks>
public sealed class MyCasesHandler(ICaseStore cases)
    : IRequestHandler<MyCasesQuery, IReadOnlyList<Case>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Case>> Handle(
        MyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return cases.ForPersonAsync(request.Subject, cancellationToken);
    }
}

/// <summary>"Show us our cases."</summary>
public sealed record CompanyCasesQuery(TenantId Company) : IQueryMessage<IReadOnlyList<Case>>;

/// <summary>The company's list — filtered per row over the ledger.</summary>
/// <remarks>
/// <para><strong>One batch question, no cache</strong> (ADR-0030, ADR-0013).
/// Whoever has revoked falls out of the list — at the next call, not at the one
/// after.</para>
///
/// <para><strong>And no total count</strong> (ADR-0026): over the difference to
/// the length it would say how many human beings have taken the view away from
/// the company. The short list is not filled up either.</para>
/// </remarks>
public sealed class CompanyCasesHandler(ICaseStore cases, IConsentGate gate)
    : IRequestHandler<CompanyCasesQuery, IReadOnlyList<Case>>
{
    /// <inheritdoc />
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    public async Task<IReadOnlyList<Case>> Handle(
        CompanyCasesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var all = await cases.ForCompanyAsync(request.Company, cancellationToken);

        if (all.Count == 0)
        {
            return all;
        }

        var allowed = await gate.MayEachSeeAsync(
            [.. all.Select(caseItem => (caseItem.Subject, request.Company))], cancellationToken);

        if (allowed.Count != all.Count)
        {
            // A mismatched answer is refused instead of guessed at: aligning it
            // wrongly would mean reading the wrong human being's release.
            throw new ConsentGateSilent(
                $"{allowed.Count} answers to {all.Count} questions");
        }

        return [.. all.Where((_, i) => allowed[i])];
    }
}
