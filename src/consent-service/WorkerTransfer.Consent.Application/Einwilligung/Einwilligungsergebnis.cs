using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Application.Einwilligung;

/// <summary>What recording a fact produced.</summary>
/// <remarks>
/// A union rather than an exception, because none of the three is exceptional:
/// a caller can ask about the wrong person, and a capability can be misspelt.
/// The endpoint maps each to its status code, and the compiler asks about a
/// fourth case if one ever appears.
/// </remarks>
public abstract record Einwilligungsergebnis
{
    private Einwilligungsergebnis()
    {
    }

    /// <summary>The fact was recorded; this is what now holds.</summary>
    public sealed record Stand(ConsentState Zustand) : Einwilligungsergebnis;

    /// <summary>
    /// The caller tried to change somebody else's consent.
    /// </summary>
    /// <remarks>
    /// Strict self-management: there is no delegation model, and admin or
    /// guardian consent is a later, deliberate decision rather than something
    /// allowed by omission (ADR-0013).
    /// </remarks>
    public sealed record FremderGegenstand : Einwilligungsergebnis;

    /// <summary>The capability or the reason does not read as one.</summary>
    /// <param name="Detail">
    /// Which of the two, in words a caller can act on — never the value they
    /// sent, which would put it in a log and a screenshot.
    /// </param>
    public sealed record Unbrauchbar(string Detail) : Einwilligungsergebnis;
}

/// <summary>What a check produced.</summary>
public abstract record Pruefergebnis
{
    private Pruefergebnis()
    {
    }

    /// <summary>What holds for the pair that was asked about.</summary>
    public sealed record Stand(ConsentState Zustand) : Pruefergebnis;

    /// <summary>The capability does not read as one.</summary>
    public sealed record Unbrauchbar(string Detail) : Pruefergebnis;
}

/// <summary>What a batch check produced.</summary>
public abstract record Sammelergebnis
{
    private Sammelergebnis()
    {
    }

    /// <summary>One state per question, in the order of the questions.</summary>
    public sealed record Staende(IReadOnlyList<ConsentState> Zustaende) : Sammelergebnis;

    /// <summary>
    /// One pair did not read, and the whole request fails.
    /// </summary>
    /// <remarks>
    /// Deliberately not "not granted" for that one pair. An unreadable
    /// identifier is a programming error at the caller, and answering it as a
    /// missing consent would hide it — until somebody sees an empty page and
    /// goes looking for the reason in the ledger.
    /// </remarks>
    public sealed record Unbrauchbar(string Detail) : Sammelergebnis;
}
