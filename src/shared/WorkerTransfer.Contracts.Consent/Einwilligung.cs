namespace WorkerTransfer.Contracts.Consent;

/// <summary>One question: may this capability be exercised for this person?</summary>
public sealed record EinwilligungsfrageV1(Guid SubjectId, string Capability);

/// <summary>
/// The answer to "may I?" — deliberately without the reason.
/// </summary>
/// <remarks>
/// <c>/consent/check</c> is open to any authenticated caller asking about any
/// subject, because that is what makes the ledger usable as an enabler. A
/// withdrawal reason is free text a person wrote about themselves, so it must
/// not ride along on a query anyone may issue: the caller learns whether a
/// capability is granted, never why it was withdrawn.
/// <para>
/// A separate type rather than a nulled-out field on the subject's own view: a
/// field that has to be blanked at the boundary gets un-blanked by the next
/// refactor.
/// </para>
/// </remarks>
public sealed record EinwilligungsantwortV1(Guid SubjectId, string Capability, bool Granted)
{
    /// <summary>Whether the capability was erased rather than withdrawn.</summary>
    public bool Deleted { get; init; }
}

/// <summary>Several "may I?" in one request.</summary>
/// <remarks>
/// The reason is measured: one page of candidates cost up to forty separate
/// calls to the ledger, each with its own connection — 1.7 to 8.8 seconds for a
/// page, and under load longer than an interface waits.
/// <para>
/// What does <em>not</em> change: the read stays synchronous, nothing is cached
/// and nothing is held in reserve (ADR-0013 — a withdrawal has to take effect on
/// the very next read). A batch is one question in one round trip, not a stock
/// answer.
/// </para>
/// </remarks>
public sealed record EinwilligungssammelfrageV1(IReadOnlyList<EinwilligungsfrageV1> Pairs);

/// <summary>
/// The answers — <em>in the order of the questions</em>, one per pair.
/// </summary>
/// <remarks>
/// The order is part of the contract and not convenience: the caller maps
/// answers onto its rows. In any other order it would have to match on
/// (subject, capability) — and a pair asked twice would then be ambiguous. A
/// consumer refuses a mismatched length rather than guessing, because
/// misaligning them shows the wrong person's data.
/// <para>
/// The same fields as the single answer, so still <em>without</em> a withdrawal
/// reason. A batch must not give away what the single question withholds.
/// </para>
/// </remarks>
public sealed record EinwilligungssammelantwortV1(IReadOnlyList<EinwilligungsantwortV1> Results);

/// <summary>The limits of the ledger's boundary.</summary>
public static class Einwilligungsgrenzen
{
    /// <summary>
    /// How many pairs a batch may carry at most.
    /// </summary>
    /// <remarks>
    /// Derived, not chosen: a consumer asks for at most one page, the largest
    /// page is fifty, and a person can be asked about two capabilities — public
    /// and company-specific. Hence one hundred.
    /// <para>
    /// It stands for a second reason. The single check is already open to any
    /// authenticated caller about any person; a batch changes nothing about
    /// that in principle but makes asking <em>cheaper</em>. A ceiling keeps the
    /// difference small: one request becomes a hundred answers, not a hundred
    /// thousand.
    /// </para>
    /// </remarks>
    public const int HoechsteSammelgroesse = 100;

    /// <summary>
    /// Never. The ledger is read synchronously and cached nowhere (ADR-0013).
    /// </summary>
    /// <remarks>
    /// Here as a named constant so a composition root that reaches for a cache
    /// has to delete this line first, and read why. A withdrawal has to take
    /// effect on the very next read — a cache here is not a performance detail
    /// but a broken promise.
    /// </remarks>
    public static readonly TimeSpan Zwischenspeicherdauer = TimeSpan.Zero;
}
