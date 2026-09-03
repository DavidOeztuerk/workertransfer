using Girder.Core.Identity;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Application.Loeschung;

/// <summary>What this service itself has to do for an erasure.</summary>
/// <remarks>
/// Split from the outbox on purpose: the outbox carries the <em>intents</em>,
/// this carries the service's own rows. Both run in one transaction, and
/// neither knows what the other holds.
/// </remarks>
/// <summary>Where the last mail goes, and in which language.</summary>
/// <remarks>
/// Both read from the row that is about to be deleted, in one go and at the
/// moment of sending. Neither travels in the outbox: that is durable storage
/// and ends up in every backup (ADR-0025 §5). The language is here rather than
/// on the request because there is no request — this runs from the dispatcher,
/// possibly days after somebody asked to be deleted.
/// </remarks>
/// <param name="Adresse">The address the notice goes to.</param>
/// <param name="Sprache">What to write it in.</param>
public sealed record Schlussanschrift(string Adresse, Kontosprache Sprache);

public interface ILoeschbestand
{
    /// <summary>Whether a cascade for this person is already under way.</summary>
    Task<bool> LaeuftBereitsAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default);

    /// <summary>Disables the account and ends every session, at once.</summary>
    Task SperreAsync(
        SubjectId wer,
        DateTimeOffset jetzt,
        CancellationToken cancellationToken = default);

    /// <summary>What is still outstanding for this person, as a query.</summary>
    /// <remarks>
    /// Empty means finished. The proof of ADR-0027 §4 is a SQL query, not a
    /// guess, and it is answerable per recipient — one sees not only
    /// <em>that</em> something is open but <em>which service</em>.
    /// <para>
    /// The company withdrawal expressly does not count: otherwise a silent
    /// jobs-service could hold a person's erasure open, and a personal right
    /// would again hang on an organisational question.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<string>> OffeneAbsichtenAsync(
        SubjectId wer,
        CancellationToken cancellationToken = default);

    /// <summary>The address, while it still exists.</summary>
    /// <remarks>
    /// Read at delivery time and never carried in the outbox. The final notice
    /// needs an address, and the address lives in the row that is about to be
    /// deleted — taking it along would be the obvious shortcut and is expressly
    /// forbidden: an outbox is durable storage and lands in every backup
    /// (ADR-0025 §5).
    /// </remarks>
    Task<Schlussanschrift?> AdresseAsync(
        SubjectId wer, CancellationToken cancellationToken = default);

    /// <summary>
    /// The last step: <c>users</c> falls, and what hangs on it.
    /// </summary>
    /// <returns>
    /// The companies left without an administrator. They are put dormant
    /// (ADR-0027 §7).
    /// </returns>
    /// <remarks>
    /// Idempotent: if the row is already gone, this step succeeded once before
    /// and there is nothing left to do.
    /// </remarks>
    Task<IReadOnlyList<TenantId>> SchliesseAbAsync(
        SubjectId wer,
        DateTimeOffset jetzt,
        CancellationToken cancellationToken = default);
}
