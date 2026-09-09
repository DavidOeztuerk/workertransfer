using WorkerTransfer.Resume.Domain.Anfragen;

namespace WorkerTransfer.Resume.Application.Anfragen;

/// <summary>How a request command ended.</summary>
public abstract record Anfrageergebnis
{
    private Anfrageergebnis()
    {
    }

    /// <summary>It worked; here is the proceeding as it now stands.</summary>
    public sealed record Erledigt(Anfrage Anfrage) : Anfrageergebnis;

    /// <summary>
    /// Hidden or non-existent — from outside the same thing.
    /// </summary>
    /// <remarks>
    /// Covers three cases on purpose: the person's profile is not released, the
    /// request id belongs to somebody else, and the request id does not exist.
    /// A caller that could tell them apart could probe for people and for
    /// proceedings by guessing ids.
    /// </remarks>
    public sealed record NichtSichtbar : Anfrageergebnis;

    /// <summary>
    /// This company already asked. Once is the whole allowance.
    /// </summary>
    /// <remarks>
    /// Holds after a refusal and after a withdrawal. Whoever may ask three
    /// times did not get a no, they got a delay — and a withdrawal is a
    /// stronger statement than a refusal, not a weaker one.
    /// </remarks>
    public sealed record SchonGefragt : Anfrageergebnis;

    /// <summary>The proceeding refused the answer. Carries the rule's code.</summary>
    public sealed record Regelverstoss(string Code, string Erklaerung) : Anfrageergebnis;
}
