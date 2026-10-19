using Noelia.Core.Identity;
using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Domain.Releases;

/// <summary>Where a release negotiation stands.</summary>
public enum NegotiationState
{
    /// <summary>Terms are on the table; the side that did not set them may accept.</summary>
    Asked,

    /// <summary>Both companies agree; the person has not yet.</summary>
    Agreed,

    /// <summary>The person agreed too. Final.</summary>
    Settled,

    /// <summary>The current employer said no. Final.</summary>
    Refused,

    /// <summary>The hiring company withdrew. Final.</summary>
    Withdrawn,

    /// <summary>The person said no. Final.</summary>
    Declined,

    /// <summary>The transfer it belonged to ended. Final.</summary>
    Closed
}

/// <summary>Which company set the terms on the table.</summary>
public enum NegotiationSide
{
    /// <summary>The company the person would move to.</summary>
    Hiring,

    /// <summary>The person's current employer.</summary>
    Current
}

/// <summary>The words a negotiation travels under on the wire.</summary>
public static class NegotiationWords
{
    /// <summary>The states in which it is still running.</summary>
    public static readonly IReadOnlyList<NegotiationState> Running =
        [NegotiationState.Asked, NegotiationState.Agreed];

    /// <summary>The word for a state.</summary>
    public static string Word(NegotiationState state) => state switch
    {
        NegotiationState.Asked => "asked",
        NegotiationState.Agreed => "agreed",
        NegotiationState.Settled => "settled",
        NegotiationState.Refused => "refused",
        NegotiationState.Withdrawn => "withdrawn",
        NegotiationState.Declined => "declined",
        NegotiationState.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The state for a word, or <c>null</c>.</summary>
    public static NegotiationState? Read(string? word) => word switch
    {
        "asked" => NegotiationState.Asked,
        "agreed" => NegotiationState.Agreed,
        "settled" => NegotiationState.Settled,
        "refused" => NegotiationState.Refused,
        "withdrawn" => NegotiationState.Withdrawn,
        "declined" => NegotiationState.Declined,
        "closed" => NegotiationState.Closed,
        _ => null
    };

    /// <summary>The word for a side.</summary>
    public static string Word(NegotiationSide side) => side switch
    {
        NegotiationSide.Hiring => "hiring",
        NegotiationSide.Current => "current",
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    /// <summary>The side for a word, or <c>null</c>.</summary>
    public static NegotiationSide? ReadSide(string? word) => word switch
    {
        "hiring" => NegotiationSide.Hiring,
        "current" => NegotiationSide.Current,
        _ => null
    };
}

/// <summary>This step is not possible from here.</summary>
public sealed class NegotiationNotNow(NegotiationState now, string what)
    : CaseError($"A {NegotiationWords.Word(now)} release negotiation cannot be {what}");

/// <summary>Something about the terms is wrong.</summary>
public sealed class TermsError(string what) : CaseError(what);

/// <summary>
/// An earlier release, negotiated between the company the person would move
/// to and her current employer (C3, D1, ADR-0050).
/// </summary>
/// <remarks>
/// <para><strong>The fee is the price of letting her go sooner</strong> — of a
/// termination agreement — and never the sale of a contract. It is paid from
/// company to company, never by the person, and the platform records it
/// without moving it (C4) and without suggesting one.</para>
///
/// <para><strong>The person decides last, and can say no first.</strong> Two
/// companies agreeing is not a result until she agrees too; she can decline in
/// every running state, also because she does not want to move at all.</para>
///
/// <para>Accepting is always the side that did <em>not</em> set the terms on
/// the table. Otherwise a company could put terms down and accept them in the
/// same breath.</para>
/// </remarks>
public sealed class ReleaseNegotiation
{
    private ReleaseNegotiation(
        Guid id,
        Guid caseId,
        SubjectId subject,
        TenantId hiring,
        TenantId current,
        NegotiationState state,
        string releaseMonth,
        long? feeCents,
        NegotiationSide termsBy,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt)
    {
        Id = id;
        CaseId = caseId;
        Subject = subject;
        Hiring = hiring;
        Current = current;
        State = state;
        ReleaseMonth = releaseMonth;
        FeeCents = feeCents;
        TermsBy = termsBy;
        CreatedAt = createdAt;
        ChangedAt = changedAt;
    }

    /// <summary>Which negotiation.</summary>
    public Guid Id { get; }

    /// <summary>The transfer it belongs to.</summary>
    public Guid CaseId { get; }

    /// <summary>Whom it is about.</summary>
    public SubjectId Subject { get; }

    /// <summary>The company she would move to — the transfer's company.</summary>
    public TenantId Hiring { get; }

    /// <summary>Her current employer, as she named it when the question was put.</summary>
    /// <remarks>
    /// Copied and not read again: if she names another employer later, a
    /// negotiation with the first does not silently move to the second.
    /// </remarks>
    public TenantId Current { get; }

    /// <summary>Where it stands.</summary>
    public NegotiationState State { get; private set; }

    /// <summary>The month she would be released to, <c>yyyy-MM</c>.</summary>
    public string ReleaseMonth { get; private set; }

    /// <summary>The fee in cents, or <c>null</c>: a release without one.</summary>
    public long? FeeCents { get; private set; }

    /// <summary>Who set the terms on the table.</summary>
    public NegotiationSide TermsBy { get; private set; }

    /// <summary>When it began.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When something last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Is it still running?</summary>
    public bool IsRunning => NegotiationWords.Running.Contains(State);

    /// <summary>The hiring company asks the current employer.</summary>
    public static ReleaseNegotiation Ask(
        Cases.Transfer caseItem, TenantId current, string? month, long? feeCents, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(caseItem);

        if (!caseItem.IsRunning)
        {
            throw new TransitionNotAllowed(caseItem.State, "taken to the current employer");
        }

        if (!caseItem.NeedsRelease)
        {
            // She said she is not employed: there is nobody to release her.
            throw new TermsError("This transfer needs no release");
        }

        if (current == caseItem.Company)
        {
            throw new TermsError("A company cannot ask itself for a release");
        }

        return new ReleaseNegotiation(
            Guid.CreateVersion7(), caseItem.Id, caseItem.Subject, caseItem.Company, current,
            NegotiationState.Asked, Month(month), Fee(feeCents), NegotiationSide.Hiring, now, now);
    }

    /// <summary>The negotiation as a row holds it.</summary>
    public static ReleaseNegotiation Restore(
        Guid id,
        Guid caseId,
        SubjectId subject,
        TenantId hiring,
        TenantId current,
        NegotiationState state,
        string releaseMonth,
        long? feeCents,
        NegotiationSide termsBy,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt) =>
        new(id, caseId, subject, hiring, current, state, releaseMonth, feeCents, termsBy,
            createdAt, changedAt);

    // --- The companies ----------------------------------------------------

    /// <summary>One side puts new terms on the table.</summary>
    public void PutTerms(NegotiationSide side, string? month, long? feeCents, DateTimeOffset now)
    {
        EnsureState(NegotiationState.Asked, "given new terms");

        ReleaseMonth = Month(month);
        FeeCents = Fee(feeCents);
        TermsBy = side;
        ChangedAt = now;
    }

    /// <summary>The other side accepts the terms on the table.</summary>
    public void Accept(NegotiationSide side, DateTimeOffset now)
    {
        EnsureState(NegotiationState.Asked, "accepted");

        if (side == TermsBy)
        {
            throw new NegotiationNotNow(State, "accepted by the side that set its terms");
        }

        MoveTo(NegotiationState.Agreed, now);
    }

    /// <summary>The current employer says no — always possible while it runs.</summary>
    public void Refuse(DateTimeOffset now)
    {
        EnsureRunning("refused");
        MoveTo(NegotiationState.Refused, now);
    }

    /// <summary>The hiring company withdraws — always possible while it runs.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        EnsureRunning("withdrawn");
        MoveTo(NegotiationState.Withdrawn, now);
    }

    // --- The person -------------------------------------------------------

    /// <summary>The person agrees to what both companies agreed.</summary>
    public void Agree(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureState(NegotiationState.Agreed, "agreed to");
        MoveTo(NegotiationState.Settled, now);
    }

    /// <summary>The person says no — in every running state.</summary>
    /// <remarks>A negotiation about her that she cannot leave would be a trap.</remarks>
    public void Decline(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureRunning("declined");
        MoveTo(NegotiationState.Declined, now);
    }

    // --- Shared -----------------------------------------------------------

    /// <summary>The transfer ended, or she closed the route: a running negotiation ends with it.</summary>
    public void Close(DateTimeOffset now)
    {
        if (IsRunning)
        {
            MoveTo(NegotiationState.Closed, now);
        }
    }

    private static string Month(string? month)
    {
        var trimmed = (month ?? string.Empty).Trim();

        // web/src/features/work/api/releases.ts (RELEASE_CASES) matches this sentence.
        return Months.IsMonth(trimmed)
            ? trimmed
            : throw new TermsError("The release month must be a month (yyyy-MM)");
    }

    private static long? Fee(long? feeCents) =>
        // web/src/features/work/api/releases.ts (RELEASE_CASES) matches this sentence.
        feeCents is < 0 ? throw new TermsError("A release fee cannot be negative") : feeCents;

    private void EnsureMine(SubjectId actor)
    {
        if (actor != Subject)
        {
            throw new NotYours();
        }
    }

    private void EnsureState(NegotiationState expected, string what)
    {
        if (State != expected)
        {
            throw new NegotiationNotNow(State, what);
        }
    }

    private void EnsureRunning(string what)
    {
        if (!IsRunning)
        {
            throw new NegotiationNotNow(State, what);
        }
    }

    private void MoveTo(NegotiationState target, DateTimeOffset now)
    {
        State = target;
        ChangedAt = now;
    }
}

/// <summary>Finds and stores release negotiations.</summary>
public interface IReleaseStore
{
    /// <summary>One negotiation, or <c>null</c>.</summary>
    Task<ReleaseNegotiation?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The negotiation of this transfer, or <c>null</c> — there is at most one.</summary>
    Task<ReleaseNegotiation?> ForCaseAsync(Guid caseId, CancellationToken cancellationToken = default);

    /// <summary>What other companies asked this employer, newest first.</summary>
    Task<IReadOnlyList<ReleaseNegotiation>> ForCurrentEmployerAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>This person's negotiations that still run.</summary>
    Task<IReadOnlyList<ReleaseNegotiation>> RunningForAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(ReleaseNegotiation negotiation, CancellationToken cancellationToken = default);
}
