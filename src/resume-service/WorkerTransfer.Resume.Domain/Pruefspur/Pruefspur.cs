using Girder.Core.Identity;

namespace WorkerTransfer.Resume.Domain.Pruefspur;

/// <summary>What happened, as the trail records it.</summary>
/// <remarks>
/// A closed set and deliberately not a free-text column: a trail whose
/// vocabulary can grow at the call site cannot be read years later without
/// reading the code that wrote it.
/// <para>
/// Only changes of state. A <em>read</em> is not in here, and that is a
/// decision: recording every time a company looked at a résumé would build a
/// record of who looked at whom, which nothing in this system reads and which
/// would be the most sensitive table in it.
/// </para>
/// </remarks>
public enum Pruefhandlung
{
    /// <summary>Somebody wrote or rewrote their own résumé.</summary>
    ResumeSaved,

    /// <summary>A company asked for one.</summary>
    ResumeRequested,

    /// <summary>The person said yes.</summary>
    RequestGranted,

    /// <summary>The person said no.</summary>
    RequestDeclined,

    /// <summary>The person took the release back.</summary>
    AccessRevoked,

    /// <summary>Everything about a person was deleted here.</summary>
    SubjectErased
}

/// <summary>One entry in the trail. Immutable once made.</summary>
/// <remarks>
/// Free of personal data by construction rather than by review: there is no
/// metadata bag at all. identity-service has one with an allowlist because it
/// records reasons and user agents; this service has nothing technical to add
/// beyond who, what, about whom and when — and a column that is always empty is
/// a column somebody eventually fills.
/// </remarks>
public sealed record Pruefeintrag(
    Pruefhandlung Handlung,
    DateTimeOffset Geschehen,
    SubjectId? Akteur = null,
    TenantId? Firma = null,
    SubjectId? Betroffene = null,
    string? Korrelation = null);

/// <summary>Appends to the trail.</summary>
/// <remarks>
/// Append only. Reading it is an operator's task, and deleting from it would
/// defeat what it is for — the account erasure clears the <em>contents</em> of
/// the affected columns instead (ADR-0027 §5).
/// <para>
/// The write joins whatever transaction the caller has open. An entry recorded
/// outside the transaction that made it can outlive a rollback or be missing
/// after a commit, and either way the trail states something that did not
/// happen.
/// </para>
/// </remarks>
public interface IPruefspur
{
    /// <param name="eintrag">What to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AppendAsync(Pruefeintrag eintrag, CancellationToken cancellationToken = default);
}
