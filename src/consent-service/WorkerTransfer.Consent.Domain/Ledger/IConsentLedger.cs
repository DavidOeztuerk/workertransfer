using Girder.Core.Identity;

namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>The log of facts, and the only way to it.</summary>
/// <remarks>
/// Note what is absent: there is no <c>update</c> and no <c>delete</c>.
/// Append-only is a property of the available methods, not a rule someone has
/// to remember — no code path exists that could rewrite history, because none
/// is offered. A test pins the absence.
/// <para>
/// Account erasure needs one exception and takes it elsewhere, under a name
/// that says why (<c>IFreitextraeumung</c>): a <c>ClearReasons</c> method here
/// would stand open to every caller, and the promise would be gone for all of
/// them.
/// </para>
/// </remarks>
public interface IConsentLedger
{
    /// <summary>Records one new fact. Never touches an existing one.</summary>
    Task AnhaengenAsync(ConsentEvent ereignis, CancellationToken cancellationToken = default);

    /// <summary>Every fact about one person, oldest first.</summary>
    Task<IReadOnlyList<ConsentEvent>> VerlaufAsync(
        SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>The newest fact for one pair, or none.</summary>
    Task<ConsentEvent?> NeuestesAsync(
        SubjectId subject, Capability capability, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest fact for many pairs, in one query.
    /// </summary>
    /// <remarks>
    /// Answered as a lookup rather than a list, so the caller does not have to
    /// map by position: a pair with no fact at all is simply missing, and
    /// absence stays a state rather than becoming a hole in a list that has to
    /// line up. The order the caller asked in is the caller's business, and the
    /// endpoint keeps it.
    /// <para>
    /// The same reduction and the same ordering as <see cref="NeuestesAsync"/>,
    /// pinned by a test.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<(Guid Subject, string Capability), ConsentEvent>> NeuesteAsync(
        IReadOnlyList<(SubjectId Subject, Capability Capability)> paare,
        CancellationToken cancellationToken = default);

    /// <summary>The newest fact for every capability one person ever held.</summary>
    /// <remarks>
    /// The same reduction as <see cref="NeuestesAsync"/>, over all capabilities
    /// instead of one.
    /// </remarks>
    Task<IReadOnlyList<ConsentEvent>> NeuesteJeFaehigkeitAsync(
        SubjectId subject, CancellationToken cancellationToken = default);
}
