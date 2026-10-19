using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Market;

/// <summary>Something about the input is wrong.</summary>
public abstract class MarketError(string notification) : Exception(notification);

/// <summary>The note is too long.</summary>
/// <remarks>
/// Not <c>NoteTooLong</c>, and that is the vocabulary doing its job: <c>note</c>
/// stands on the forbidden list because the German word is the most dangerous
/// one a placement platform can have (ADR-0022). The wire field has to be
/// called <c>note</c> and carries one of the thirteen exemptions; a type name
/// does not have to, so it does not get a fourteenth.
/// </remarks>
public sealed class RemarkTooLong(int limit)
    // web/src/features/work/pages/MarketPage.tsx (SAVE_CASES) matches "note exceeds".
    : MarketError($"The note exceeds {limit} characters");

/// <summary>How approachable somebody is.</summary>
/// <remarks>
/// Three states, <strong>every transition allowed</strong>. It is a statement
/// about one's own will, and that changes without an order. A state machine
/// with prohibitions would be paternalism here: nobody has to have been "open"
/// first in order to become "listening".
/// </remarks>
public enum Availability
{
    /// <summary>Looking.</summary>
    Open,

    /// <summary>Listening.</summary>
    Listening,

    /// <summary>Not approachable.</summary>
    Unavailable
}

/// <summary>The words an availability travels under on the wire.</summary>
public static class Availabilities
{
    /// <summary>The word for a state.</summary>
    public static string Word(Availability value) => value switch
    {
        Availability.Open => "open",
        Availability.Listening => "listening",
        Availability.Unavailable => "unavailable",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    /// <summary>The state for a word, or <c>null</c>.</summary>
    public static Availability? Read(string? word) => word switch
    {
        "open" => Availability.Open,
        "listening" => Availability.Listening,
        "unavailable" => Availability.Unavailable,
        _ => null
    };
}

/// <summary>A person's market status — "am I approachable?"</summary>
/// <remarks>
/// <strong>The most dangerous statement in the whole system.</strong> A CV
/// gives away where somebody has been; the market status gives away that they
/// want to leave — and the mere <em>existence</em> of the statement can cost
/// somebody their job.
/// <para>
/// So there is deliberately <strong>no <c>:public</c></strong>: the release
/// always names a recipient. On the profile "to every company" is a sensible
/// choice; here it would be a switch whose consequences nobody can survey —
/// among them one's own employer, who is on the same platform.
/// </para>
/// </remarks>
public sealed class MarketStatus
{
    /// <summary>How long the note may be.</summary>
    public const int MaxNoteLength = 500;

    private MarketStatus(
        SubjectId subject,
        Availability availability,
        bool isEmployed,
        string remark,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt)
    {
        Subject = subject;
        Availability = availability;
        IsEmployed = isEmployed;
        Remark = remark;
        CreatedAt = createdAt;
        ChangedAt = changedAt;
    }

    /// <summary>Whose status. It <em>is</em> the key.</summary>
    public SubjectId Subject { get; }

    /// <summary>How approachable.</summary>
    public Availability Availability { get; private set; }

    /// <summary>Am I working somewhere right now?</summary>
    /// <remarks>
    /// A field, not a state: it is no intention. One can be employed <em>and</em>
    /// open — that is the ordinary case on a transfer market, and as a state it
    /// would be impossible.
    /// </remarks>
    public bool IsEmployed { get; private set; }

    /// <summary>What the person writes about it.</summary>
    public string Remark { get; private set; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>May this person be approached?</summary>
    /// <remarks>
    /// <c>Unavailable</c> means no — for a company with a release too.
    /// <strong>The release permits seeing, not disturbing.</strong>
    /// </remarks>
    public bool IsApproachable =>
        Availability is Availability.Open or Availability.Listening;

    /// <summary>Whoever has said nothing has not said "I am listening".</summary>
    /// <remarks>
    /// The default must never fall in the market's favour.
    /// </remarks>
    public static MarketStatus Default(SubjectId subject, DateTimeOffset now) =>
        new(subject, Availability.Unavailable, isEmployed: false, string.Empty, now, now);

    /// <summary>Creates a status.</summary>
    /// <exception cref="RemarkTooLong">The note is too long.</exception>
    public static MarketStatus Create(
        SubjectId subject,
        Availability availability,
        bool isEmployed,
        string remark,
        DateTimeOffset now) =>
        new(subject, availability, isEmployed, Text(remark), now, now);

    /// <summary>The status as a row holds it.</summary>
    public static MarketStatus Restore(
        SubjectId subject,
        Availability availability,
        bool isEmployed,
        string remark,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt) =>
        new(subject, availability, isEmployed, remark, createdAt, changedAt);

    /// <summary>Writes the status anew.</summary>
    /// <exception cref="RemarkTooLong">The note is too long.</exception>
    public void Change(
        Availability availability,
        bool isEmployed,
        string remark,
        DateTimeOffset now)
    {
        // Check first, then write: a refused form must not leave a half-changed
        // aggregate behind.
        var checkedValue = Text(remark);

        Availability = availability;
        IsEmployed = isEmployed;
        Remark = checkedValue;
        ChangedAt = now;
    }

    private static string Text(string? remark)
    {
        var trimmed = (remark ?? string.Empty).Trim();

        return trimmed.Length > MaxNoteLength
            ? throw new RemarkTooLong(MaxNoteLength)
            : trimmed;
    }
}

/// <summary>Finds and stores market statuses.</summary>
public interface IMarketStore
{
    /// <summary>This person's status, or <c>null</c>.</summary>
    Task<MarketStatus?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(MarketStatus status, CancellationToken cancellationToken = default);
}
