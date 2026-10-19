namespace WorkerTransfer.Applications.Application.Erasure;

/// <summary>The switch that stands at OFF (ADR-0027 §3).</summary>
/// <remarks>
/// <strong>The default erases completely, <c>status = 'hired'</c> included.</strong>
/// That is the load-bearing decision of this service, and a decision about the
/// <em>burden of proof</em>: it is not the erasure that must justify itself but
/// the keeping.
/// <para>
/// The assessment carrying it (the client, 06.08.2026, expressly as an
/// assessment and not as legal advice): the platform is not the employer.
/// Retention duties for employment contracts fall on the company with
/// <em>its own</em> records, not on an intermediary. For application data
/// rather the opposite of a retention duty applies: a short grace period
/// because of the AGG deadline for claims, then it is to be erased. Open is
/// exactly one sentence a lawyer has to confirm; it does not change this design,
/// only the position of the switch — and therefore blocks nothing.
/// </para>
/// <para>
/// <strong>What this means for the company, said openly:</strong> when a person
/// erases their account, the application through which they were hired also
/// disappears from the company's list. That is intended, and
/// <c>/delete-account</c> says so before the button.
/// </para>
/// </remarks>
public static class Retention
{
    /// <summary><strong>OFF.</strong> Hired applications fall with the rest.</summary>
    /// <remarks>
    /// A named constant and <strong>not a configuration value</strong>: with an
    /// erasure promise, "different in production than in the test" would be the
    /// worst state imaginable. Flipping it is a visible commit somebody has to
    /// justify — not an environment variable nobody reads.
    /// <para>
    /// It switches <strong>exactly one row class in this service</strong>:
    /// <c>status = 'hired'</c>. No extension to <c>rejected</c> — a rejected
    /// application justifies nothing — and no "running case" as a rubber word.
    /// And no period, in either direction: neither a guessed duration nor a
    /// follow-up that cleans up later. If it is ever flipped, the period comes
    /// <em>together with the answer</em>, not before.
    /// </para>
    /// <para>
    /// The <see cref="Ports.IErasureLedger"/> takes it as a parameter instead of
    /// reading it itself. Otherwise one could only check that it stands at off —
    /// not what the flipped switch covers, and that is exactly the statement
    /// that matters.
    /// </para>
    /// <para>
    /// <c>static readonly</c> and not <c>const</c>, for a measured reason: a
    /// <c>const</c> is copied into every reading assembly. During a counter-probe
    /// it stood at <c>false</c> here again while the test assembly still carried
    /// the old <c>true</c> — a test that falls over unchanged code is worse than
    /// none. A field is read where it stands.
    /// </para>
    /// </remarks>
    public static readonly bool KeepHired = false;
}
