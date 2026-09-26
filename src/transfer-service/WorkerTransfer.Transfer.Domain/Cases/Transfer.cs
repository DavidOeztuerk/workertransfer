using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Cases;

/// <summary>Where a case stands.</summary>
public enum TransferState
{
    /// <summary>The company has shown interest.</summary>
    Interested,

    /// <summary>They are talking.</summary>
    Talking,

    /// <summary>An offer is on the table.</summary>
    Offered,

    /// <summary>The person has accepted.</summary>
    Accepted,

    /// <summary>Completed.</summary>
    Completed,

    /// <summary>Declined by the person.</summary>
    Declined,

    /// <summary>Withdrawn by the company.</summary>
    Withdrawn
}

/// <summary>The words a transfer state travels under on the wire.</summary>
public static class TransferStates
{
    /// <summary>The word for a state.</summary>
    public static string Word(TransferState state) => state switch
    {
        TransferState.Interested => "interested",
        TransferState.Talking => "talking",
        TransferState.Offered => "offered",
        TransferState.Accepted => "accepted",
        TransferState.Completed => "completed",
        TransferState.Declined => "declined",
        TransferState.Withdrawn => "withdrawn",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The state for a word, or <c>null</c>.</summary>
    public static TransferState? Read(string? word) => word switch
    {
        "interested" => TransferState.Interested,
        "talking" => TransferState.Talking,
        "offered" => TransferState.Offered,
        "accepted" => TransferState.Accepted,
        "completed" => TransferState.Completed,
        "declined" => TransferState.Declined,
        "withdrawn" => TransferState.Withdrawn,
        _ => null
    };

    /// <summary>The states in which a case is still running.</summary>
    /// <remarks>
    /// Stands here and not only in the aggregate, because the partial index in
    /// the database needs the same set: exactly one running case per (person,
    /// company).
    /// </remarks>
    public static readonly IReadOnlyList<TransferState> Running =
    [
        TransferState.Interested,
        TransferState.Talking,
        TransferState.Offered,
        TransferState.Accepted
    ];
}

/// <summary>Something about the input is wrong.</summary>
public abstract class CaseError(string notification) : Exception(notification);

/// <summary>A text field is too long.</summary>
public sealed class TextError(string field, int limit)
    // web/src/features/work/api/transfers.ts (COMPANY_TRANSFER_CASES) matches this sentence.
    : CaseError($"{field} exceeds {limit} characters");

/// <summary>Something about the offer is wrong.</summary>
public sealed class OfferError(string what) : CaseError(what);

/// <summary>That is another human being's case.</summary>
public sealed class NotYours() : CaseError("This transfer belongs to someone else");

/// <summary>This step is not possible from here.</summary>
public sealed class TransitionNotAllowed(TransferState now, string what)
    : CaseError($"A {TransferStates.Word(now)} transfer cannot be {what}");

/// <summary>The transfer case — three yeses, a no at any time.</summary>
/// <remarks>
/// The ULTRAPLAN demands "employed → the company has to take part". That cannot
/// be built that way: <strong>the platform does not know where somebody
/// works.</strong> <c>IsEmployed</c> is a boolean the person sets themselves;
/// there is no record "Anna works at X".
/// <para>
/// And there must not be one. It would be the link between "works at X" and
/// "is listening" — exactly the disclosure that costs somebody their job, in a
/// single table. Creating it so the platform can write to the employer would
/// mean producing the system's greatest risk in order to enable a courtesy.
/// </para>
/// <para>
/// Instead: the case carries that a release is <em>needed</em>, and the person
/// themselves confirms that it is there. That is weaker (nobody checks it) and
/// safer. Between an assurance nobody can redeem and one that endangers
/// nobody, the second is the more honest.
/// </para>
/// </remarks>
public sealed class Transfer
{
    /// <summary>How long message and offer text may be.</summary>
    public const int MaxTextLength = 2000;

    private Transfer(
        Guid id,
        SubjectId subject,
        TenantId company,
        TransferState state,
        bool needsRelease,
        bool releaseConfirmed,
        string message,
        string offerText,
        string? offerStart,
        long? offerFeeCents,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt)
    {
        Id = id;
        Subject = subject;
        Company = company;
        State = state;
        NeedsRelease = needsRelease;
        ReleaseConfirmed = releaseConfirmed;
        Message = message;
        OfferText = offerText;
        OfferStart = offerStart;
        OfferFeeCents = offerFeeCents;
        CreatedAt = createdAt;
        ChangedAt = changedAt;
    }

    /// <summary>Which case.</summary>
    public Guid Id { get; }

    /// <summary>Whom it is about.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which company.</summary>
    public TenantId Company { get; }

    /// <summary>Where it stands.</summary>
    public TransferState State { get; private set; }

    /// <summary>Whether a release from the current employer is needed.</summary>
    /// <remarks>
    /// Copied from the market status at creation, not fetched again at every
    /// read: whoever resigns during a running conversation does not thereby
    /// change the terms of an offer retroactively — and a company cannot hope
    /// the rule will still change.
    /// </remarks>
    public bool NeedsRelease { get; }

    /// <summary>Confirmed by the <em>person</em>.</summary>
    /// <remarks>The platform does not check it and cannot.</remarks>
    public bool ReleaseConfirmed { get; private set; }

    /// <summary>What the company wrote.</summary>
    public string Message { get; }

    /// <summary>What stands in the offer.</summary>
    public string OfferText { get; private set; }

    /// <summary>When it is meant to start, or <c>null</c>.</summary>
    public string? OfferStart { get; private set; }

    /// <summary>The agreed placement fee in cents, or <c>null</c>.</summary>
    /// <remarks>
    /// Recorded, not moved: the platform holds no money. It is a number two
    /// companies agree on, and it stands here so both sides have the same one
    /// in view.
    /// </remarks>
    public long? OfferFeeCents { get; private set; }

    /// <summary>When it began.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When something last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Is it still running?</summary>
    /// <remarks>
    /// Final means final. Whoever wants another go begins a new case — and needs
    /// a valid release of the market status again for it.
    /// </remarks>
    public bool IsRunning => TransferStates.Running.Contains(State);

    /// <summary>A company shows interest.</summary>
    public static Transfer ShowInterest(
        SubjectId subject,
        TenantId company,
        bool needsRelease,
        string message,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, TransferState.Interested, needsRelease,
            releaseConfirmed: false, Text("Message", message), string.Empty,
            null, null, now, now);

    /// <summary>The case as a row holds it.</summary>
    public static Transfer Restore(
        Guid id,
        SubjectId subject,
        TenantId company,
        TransferState state,
        bool needsRelease,
        bool releaseConfirmed,
        string message,
        string offerText,
        string? offerStart,
        long? offerFeeCents,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt) =>
        new(id, subject, company, state, needsRelease, releaseConfirmed, message,
            offerText, offerStart, offerFeeCents, createdAt, changedAt);

    // --- The person ------------------------------------------------------

    /// <summary>The person agrees to a conversation.</summary>
    public void AcceptConversation(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureState(TransferState.Interested, "opened for talks");
        MoveTo(TransferState.Talking, now);
    }

    /// <summary>The person accepts the offer.</summary>
    public void AcceptOffer(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureState(TransferState.Offered, "accepted");
        MoveTo(TransferState.Accepted, now);
    }

    /// <summary>The person confirms that their employer lets them go.</summary>
    /// <remarks>
    /// Nobody checks that, and nobody can: the platform knows neither the
    /// employer nor the contract. What the step achieves is asking the question
    /// and recording the answer.
    /// </remarks>
    public void ConfirmRelease(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureState(TransferState.Accepted, "released");

        if (!NeedsRelease)
        {
            throw new TransitionNotAllowed(State, "released without needing a release");
        }

        ReleaseConfirmed = true;
        MoveTo(TransferState.Completed, now);
    }

    /// <summary>The person declines.</summary>
    /// <remarks>
    /// Always possible, from every running state. A process one cannot get out
    /// of is no process but a trap.
    /// </remarks>
    public void Decline(SubjectId actor, DateTimeOffset now)
    {
        EnsureMine(actor);
        EnsureRunning("declined");
        MoveTo(TransferState.Declined, now);
    }

    // --- The company -----------------------------------------------------

    /// <summary>The company makes an offer.</summary>
    public void MakeOffer(
        string text, string? start, long? feeCents, DateTimeOffset now)
    {
        EnsureState(TransferState.Talking, "offered");

        if (feeCents is < 0)
        {
            // web/src/features/work/api/transfers.ts (COMPANY_TRANSFER_CASES) matches this sentence.
            throw new OfferError("A transfer fee cannot be negative");
        }

        OfferText = Text("Offer", text);
        OfferStart = start;
        OfferFeeCents = feeCents;
        MoveTo(TransferState.Offered, now);
    }

    /// <summary>The company completes.</summary>
    /// <remarks>
    /// Completing is the statement "we are hiring" — and that is the employer's
    /// to make. The person has already said yes with
    /// <see cref="AcceptOffer"/>.
    /// </remarks>
    public void Complete(DateTimeOffset now)
    {
        EnsureState(TransferState.Accepted, "completed");

        if (NeedsRelease)
        {
            // For as long as a release is needed, the person completes — they
            // are the only one who knows whether it is there.
            throw new TransitionNotAllowed(State, "completed before the release is confirmed");
        }

        MoveTo(TransferState.Completed, now);
    }

    /// <summary>The company withdraws.</summary>
    public void Withdraw(DateTimeOffset now)
    {
        EnsureRunning("withdrawn");
        MoveTo(TransferState.Withdrawn, now);
    }

    // --- Shared ----------------------------------------------------------

    private static string Text(string field, string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length > MaxTextLength
            ? throw new TextError(field, MaxTextLength)
            : trimmed;
    }

    private void EnsureMine(SubjectId principal)
    {
        if (principal != Subject)
        {
            throw new NotYours();
        }
    }

    private void EnsureState(TransferState expected, string what)
    {
        if (State != expected)
        {
            throw new TransitionNotAllowed(State, what);
        }
    }

    private void EnsureRunning(string what)
    {
        if (!IsRunning)
        {
            throw new TransitionNotAllowed(State, what);
        }
    }

    private void MoveTo(TransferState target, DateTimeOffset now)
    {
        State = target;
        ChangedAt = now;
    }
}

/// <summary>Finds and stores cases.</summary>
public interface ITransferStore
{
    /// <summary>One case, or <c>null</c>.</summary>
    Task<Transfer?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The running case between these two, if there is one.</summary>
    Task<Transfer?> GetCurrentAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Transfer caseItem, CancellationToken cancellationToken = default);

    /// <summary>This person's cases.</summary>
    Task<IReadOnlyList<Transfer>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>This company's cases.</summary>
    Task<IReadOnlyList<Transfer>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);
}
