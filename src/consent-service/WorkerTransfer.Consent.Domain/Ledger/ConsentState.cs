namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>The effective state of one (subject, capability) pair.</summary>
/// <param name="Granted">Whether the permission holds right now.</param>
/// <param name="Deleted">Whether it ended in an erasure rather than a withdrawal.</param>
/// <param name="Reason">
/// Why it was withdrawn — free text the person wrote about themselves.
/// </param>
/// <remarks>
/// Carries the reason, so it may only be handed to the subject themselves. The
/// cross-subject read answers with a type that has no such field, rather than
/// with this one blanked out: a field that must be emptied at the boundary gets
/// un-emptied by the next refactor.
/// </remarks>
public sealed record ConsentState(bool Granted, bool Deleted = false, string? Reason = null)
{
    /// <summary>What a pair nobody ever touched says.</summary>
    /// <remarks>
    /// Absence is a state, not an error: a capability nobody granted is simply
    /// not granted. Consuming services must be able to ask about anything, so
    /// this is a <c>200</c> and never a <c>404</c>.
    /// </remarks>
    public static ConsentState Abwesend { get; } = new(false, false, "no consent event");
}

/// <summary>The rules, as one pure function over the facts.</summary>
/// <remarks>
/// The ledger keeps no status table. This is the canonical definition of what
/// "granted" means; the repository does the same reduction in SQL for the read
/// path, and a test pins that the two cannot disagree. Two ways to the same
/// answer that can differ are worse than no second way.
/// </remarks>
public static class Projektion
{
    /// <summary>Reduces a stream of facts to what currently holds.</summary>
    /// <param name="ereignisse">Facts about one pair, in any order.</param>
    public static ConsentState Stand(IEnumerable<ConsentEvent> ereignisse)
    {
        ArgumentNullException.ThrowIfNull(ereignisse);

        ConsentEvent? neuestes = null;

        foreach (var ereignis in ereignisse)
        {
            // Ordering is (recorded_at, event_id): two facts written in the same
            // clock tick still resolve the same way every time, instead of
            // depending on the order rows came back in.
            if (neuestes is null
                || ereignis.RecordedAt > neuestes.RecordedAt
                || (ereignis.RecordedAt == neuestes.RecordedAt
                    && Spaeter(ereignis.EventId, neuestes.EventId)))
            {
                neuestes = ereignis;
            }
        }

        return neuestes switch
        {
            null => ConsentState.Abwesend,

            // A grant after a withdrawal is a valid re-consent: withdrawing must
            // never be a one-way door for the person it belongs to.
            { Action: ConsentAction.Grant } => new ConsentState(true),

            { Action: ConsentAction.Revoke } => new ConsentState(false, false, neuestes.Reason?.Value),

            _ => new ConsentState(false, true, neuestes.Reason?.Value)
        };
    }

    /// <summary>
    /// Which of two ids sorts later — by the same rule Postgres uses.
    /// </summary>
    /// <remarks>
    /// Not <see cref="Guid.CompareTo(System.Guid)"/>. .NET compares an id
    /// field by field, Postgres compares the sixteen bytes in canonical order,
    /// and the two disagree for perfectly ordinary ids. The reduction in SQL
    /// ties on <c>event_id DESC</c>, so this function has to mean the same
    /// thing or the two ways to the same answer can differ — which is exactly
    /// the disagreement the tie-break exists to prevent. Comparing the
    /// canonical text ordinally is byte order.
    /// </remarks>
    private static bool Spaeter(Guid kandidat, Guid bisher) =>
        string.CompareOrdinal(kandidat.ToString("D"), bisher.ToString("D")) > 0;
}
