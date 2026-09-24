using Noelia.Core.Identity;
using WorkerTransfer.Advisor.Domain.Mandates;

namespace WorkerTransfer.Advisor.Domain.Conversations;

/// <summary>This step is not possible from here.</summary>
public sealed class TransitionNotAllowed(ConversationState now, string what)
    : Exception($"A {ConversationStates.Word(now)} conversation cannot be {what}");

/// <summary>This is another human being's conversation.</summary>
public sealed class NotYours() : Exception("This conversation belongs to someone else");

/// <summary>Where a conversation stands.</summary>
/// <remarks>
/// Four states, and none of them is a stage. The state says whether people are
/// still talking; the <see cref="Stage"/> says how far it is open — and that
/// comes from the ledger, not from here.
/// </remarks>
public enum ConversationState
{
    /// <summary>They are talking.</summary>
    Talking,

    /// <summary>The person agrees. Now it may be handed over.</summary>
    Agreed,

    /// <summary>Handed over to transfer-service. Here it ends with that.</summary>
    HandedOver,

    /// <summary>Ended — by whichever side.</summary>
    Ended
}

/// <summary>The words with which a state stands on the wire.</summary>
public static class ConversationStates
{
    /// <summary>The word for the state.</summary>
    public static string Word(ConversationState state) => state switch
    {
        ConversationState.Talking => "talking",
        ConversationState.Agreed => "agreed",
        ConversationState.HandedOver => "handed_over",
        ConversationState.Ended => "ended",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    /// <summary>The states in which people are still talking.</summary>
    public static readonly IReadOnlyList<ConversationState> Running =
    [
        ConversationState.Talking, ConversationState.Agreed
    ];
}

/// <summary>
/// A conversation between a human being and a company.
/// </summary>
/// <remarks>
/// <para><strong>It lives here as long as it is about stages and mandates</strong>
/// (ADR-0037 decision 4). Only the agreement becomes a case in
/// transfer-service — and the triangle consensus lives there and is expressly
/// <em>not</em> rebuilt here.</para>
///
/// <para><strong>No stage column.</strong> A <c>hasStage</c> would be the stage
/// as a second door beside the ledger; the stage is asked there on every read.
/// What this aggregate holds is the relation and its state, nothing else.</para>
///
/// <para><strong>And no message history.</strong> ADR-0037 decision 5 leaves
/// open whether people type here; as long as that is open there is no table for
/// it — and therefore no question whether an AI may write into it. The
/// <see cref="Occasion"/> is the one field the company writes when it opens.</para>
/// </remarks>
public sealed class Conversation
{
    /// <summary>How long the occasion may be.</summary>
    public const int MaxOccasionLength = 2000;

    private Conversation(
        Guid id,
        SubjectId subject,
        TenantId company,
        ConversationState state,
        string occasion,
        DateTimeOffset openedAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        Subject = subject;
        Company = company;
        State = state;
        Occasion = occasion;
        OpenedAt = openedAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Which conversation.</summary>
    public Guid Id { get; }

    /// <summary>With whom.</summary>
    public SubjectId Subject { get; }

    /// <summary>Which company.</summary>
    public TenantId Company { get; }

    /// <summary>Where it stands.</summary>
    public ConversationState State { get; private set; }

    /// <summary>What the company wrote when it opened the conversation.</summary>
    public string Occasion { get; }

    /// <summary>When it began.</summary>
    public DateTimeOffset OpenedAt { get; }

    /// <summary>When something last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Is it still running?</summary>
    public bool IsRunning => ConversationStates.Running.Contains(State);

    /// <summary>A company opens a conversation.</summary>
    /// <exception cref="InputError">The occasion is too long.</exception>
    public static Conversation Open(
        SubjectId subject, TenantId company, string? occasion, DateTimeOffset now) =>
        new(Guid.CreateVersion7(), subject, company, ConversationState.Talking,
            Clean(occasion), now, now);

    /// <summary>The conversation as a row holds it.</summary>
    public static Conversation Restore(
        Guid id,
        SubjectId subject,
        TenantId company,
        ConversationState state,
        string occasion,
        DateTimeOffset openedAt,
        DateTimeOffset updatedAt) =>
        new(id, subject, company, state, occasion, openedAt, updatedAt);

    /// <summary>The person agrees.</summary>
    /// <remarks>
    /// Only the person, and that is the whole point: the agreement a case grows
    /// out of belongs to the human being it is about.
    /// </remarks>
    public void Agree(SubjectId by, DateTimeOffset now)
    {
        EnsureOwner(by);
        Expect(ConversationState.Talking, "agreed to");
        MoveTo(ConversationState.Agreed, now);
    }

    /// <summary>
    /// The company turns it into a case.
    /// </summary>
    /// <remarks>
    /// <strong>Only after the person has agreed</strong> — and that is why the
    /// handover is the company's step: the case in transfer-service begins with
    /// a company showing interest, and there its own conditions apply (market
    /// release, approachability). None of that is repeated here.
    /// </remarks>
    public void HandOver(DateTimeOffset now)
    {
        Expect(ConversationState.Agreed, "handed over");
        MoveTo(ConversationState.HandedOver, now);
    }

    /// <summary>Somebody ends the conversation.</summary>
    /// <remarks>
    /// From every running state, by either side. A procedure one cannot leave
    /// is not a procedure but a trap.
    /// </remarks>
    public void End(DateTimeOffset now)
    {
        if (!IsRunning)
        {
            throw new TransitionNotAllowed(State, "ended");
        }

        MoveTo(ConversationState.Ended, now);
    }

    private static string Clean(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length > MaxOccasionLength
            ? throw new InputError(
                $"the opening note exceeds {MaxOccasionLength} characters")
            : trimmed;
    }

    private void EnsureOwner(SubjectId actor)
    {
        if (actor != Subject)
        {
            throw new NotYours();
        }
    }

    private void Expect(ConversationState wanted, string what)
    {
        if (State != wanted)
        {
            throw new TransitionNotAllowed(State, what);
        }
    }

    private void MoveTo(ConversationState target, DateTimeOffset now)
    {
        State = target;
        UpdatedAt = now;
    }
}

/// <summary>Finds and stores conversations.</summary>
public interface IConversationStore
{
    /// <summary>A conversation, or <c>null</c>.</summary>
    Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The running conversation between these two, if there is one.</summary>
    /// <remarks>
    /// Exactly one per (human being, company) — otherwise two stage readings
    /// would stand side by side that mean the same capabilities.
    /// </remarks>
    Task<Conversation?> GetRunningAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);

    /// <summary>This person's conversations, newest first.</summary>
    Task<IReadOnlyList<Conversation>> ForPersonAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>This company's conversations, newest first.</summary>
    Task<IReadOnlyList<Conversation>> ForCompanyAsync(
        TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Everything this service holds about a human being.</summary>
    /// <returns>How much deliberately stayed behind. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
