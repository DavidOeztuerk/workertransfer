using WorkerTransfer.Transfer.Domain.Cases;

namespace WorkerTransfer.Transfer.Application.Erasure;

/// <summary>The second switch that stands at OFF (ADR-0027 §3).</summary>
/// <remarks>
/// <strong>The default erases paid transfers too.</strong> An amount two
/// companies agreed on does not turn the row into an agency's record: the
/// platform holds no money, it records a number so both sides have the same one
/// in view.
/// </remarks>
public static class Retention
{
    /// <summary><strong>OFF.</strong> Paid cases fall with the rest.</summary>
    /// <remarks>
    /// A named constant and <strong>no configuration value</strong>: with a
    /// deletion promise, "different in production than in test" would be the
    /// worst possible state.
    /// <para>
    /// It switches <strong>exactly one row class in this service</strong>: a
    /// completed deal <em>with</em> a fee — since ADR-0050 the fee two companies
    /// agreed in a settled release negotiation, which is kept along with it. No extension to
    /// <c>interested</c>/<c>talking</c>/<c>offered</c> — a conversation is no
    /// contract — and no "running case" as a rubber word. Without a fee no
    /// trade has come into being that anything could hang on. And no period, in
    /// either direction: if it is ever flipped, the period comes <em>together
    /// with the answer</em>.
    /// </para>
    /// <para>
    /// <c>static readonly</c> and not <c>const</c>, for the same measured reason
    /// as at applications-service: a <c>const</c> is copied into every reading
    /// assembly, and a counter-probe fell over unchanged code because of it.
    /// </para>
    /// </remarks>
    public static readonly bool KeepPaid = false;

    /// <summary>The states a paid case could hang on.</summary>
    /// <remarks>
    /// <strong>Not</strong> the aggregate's final states: those contain
    /// <c>declined</c> and <c>withdrawn</c>, and a declined case grounds
    /// nothing. Written out, because the delimiting is the whole point
    /// (ADR-0027 §3.2).
    /// </remarks>
    public static readonly IReadOnlyList<TransferState> Completed =
        [TransferState.Accepted, TransferState.Completed];
}
