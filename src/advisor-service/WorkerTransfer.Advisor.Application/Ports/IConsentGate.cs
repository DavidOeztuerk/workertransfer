using Noelia.Core.Identity;
using WorkerTransfer.Advisor.Domain.Conversations;

namespace WorkerTransfer.Advisor.Application.Ports;

/// <summary>The ledger did not answer — we simply do not know.</summary>
/// <remarks>
/// Expressly <em>not</em> <c>Stage.None</c>. "I do not know" is something other
/// than "nothing released", and treating the two alike would turn an outage
/// into a statement about a human being. The endpoint turns it into 503 — not
/// 404, because that would claim there is nothing, and not "show", because that
/// would claim the opposite (ADR-0020 §3).
/// </remarks>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class ConsentGateSilent(string reason) : Exception(reason);

/// <summary>The ledger refused the release.</summary>
/// <remarks>
/// Kept apart from silence, because it is something else: the ledger answered,
/// and it said no. That happens exactly when somebody wanted to release for a
/// different human being — the ledger is strictly self-administered.
/// </remarks>
/// <param name="reason">The kind of failure, never its content.</param>
public sealed class ConsentRefused(string reason) : Exception(reason);

/// <summary>Reads and writes stages — both only through the ledger.</summary>
/// <remarks>
/// <para><strong>Synchronous, per call, without any cache</strong>
/// (ADR-0013). A revocation has to take effect on the next read, not the one
/// after. In a service whose whole purpose is staged visibility, a cache here
/// would not be a performance detail but the breach of the promise.</para>
///
/// <para><strong>Writes use the person's token.</strong> The ledger is strictly
/// self-administered — it answers 403 "a consent belongs to its subject" when
/// somebody grants for someone else. That this service asks and writes on the
/// caller's behalf is therefore not a convenience but the place where a stage
/// release becomes unforgeable: it can only succeed when the person triggers it
/// themselves.</para>
/// </remarks>
public interface IConsentGate
{
    /// <summary>
    /// Which stage currently stands — <strong>one question, all capabilities</strong>.
    /// </summary>
    /// <remarks>
    /// As a batch question (ADR-0030): one question per capability would be five
    /// connections for one row, and a list of conversations would multiply that.
    /// The answers come back in the order of the questions, and a differing
    /// length is an <em>error</em> and no reason to guess.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    Task<IReadOnlyList<Stage>> StagesAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>Grants the capabilities of a stage.</summary>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    /// <exception cref="ConsentRefused">The ledger says no.</exception>
    Task GrantAsync(
        IReadOnlyList<string> capabilities, CancellationToken cancellationToken = default);

    /// <summary>Takes capabilities back.</summary>
    /// <param name="reason">
    /// The ledger demands one — a revocation must be explainable. What this
    /// service writes is a <em>shape</em> and never a sentence about a human
    /// being.
    /// </param>
    /// <exception cref="ConsentGateSilent">The ledger does not answer.</exception>
    /// <exception cref="ConsentRefused">The ledger says no.</exception>
    Task RevokeAsync(
        IReadOnlyList<string> capabilities,
        string reason,
        CancellationToken cancellationToken = default);
}
