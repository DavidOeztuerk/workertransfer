using Noelia.Core.Identity;
using WorkerTransfer.Scout.Domain.Hits;
using WorkerTransfer.Scout.Domain.Searches;

namespace WorkerTransfer.Scout.Application.Ports;

/// <summary>The profile search did not answer.</summary>
/// <remarks>
/// As with the ledger: the kind, never the content — and no empty result. "We
/// could not search just now" is something other than "there is nobody", and an
/// empty list would look like the second statement.
/// </remarks>
/// <param name="reason">The kind of the failure.</param>
public sealed class ProfileSearchSilent(string reason) : Exception(reason);

/// <summary>A profile as profile-service reports it.</summary>
/// <remarks>
/// <para>Only what the person wrote themselves. No address, no name, no
/// employer — this service does not need them and is therefore not to get
/// them.</para>
///
/// <para><see cref="Named"/> is the provenance class <em>named</em> of ADR-0033
/// and the only searchable one. Evidence does not stand here: it is fetched to
/// the hit only afterwards.</para>
/// </remarks>
public sealed record ProfileHit(
    SubjectId Subject,
    string Headline,
    string Bio,
    string Location,
    bool RemoteOk,
    IReadOnlyList<string> Named,
    /// <summary>What they said about their commute, or <c>null</c> (ADR-0041).</summary>
    CommuteReadiness? Commute = null);

/// <summary>A page of profiles, in the order profile-service keeps.</summary>
/// <remarks>
/// <strong>Without a total.</strong> The order is <c>updated_at DESC,
/// id DESC</c> — stable and unrelated to the matter, so that two identical
/// searches give the same and nothing about a human being follows from the
/// order (ADR-0036 condition 1).
/// </remarks>
public sealed record ProfileHitPage(IReadOnlyList<ProfileHit> Items, string? Next);

/// <summary>Asks profile-service for profiles that carry the named words.</summary>
/// <remarks>
/// <para><strong>The filtering stays where the profiles are</strong> (ADR-0004:
/// no cross-service store). This service holds no copy of the profiles — a copy
/// would go stale against every change and be a second truth at the first
/// revocation.</para>
///
/// <para><strong>profile-service expressly does not check the release
/// here.</strong> It is asked one line further up, through
/// <see cref="IConsentGate"/> and for the whole page at once — that is the hard
/// part of <c>GET /candidates</c>, carried over and not reinvented (ADR-0036
/// decision 1). The call therefore goes through a service-to-service door
/// behind the shared secret and has no gateway route.</para>
/// </remarks>
public interface IProfileSearch
{
    /// <param name="filter">Only named words, location, remote.</param>
    /// <param name="pageLength">How many rows at most.</param>
    /// <param name="cursor">Where it continues, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels.</param>
    /// <exception cref="ProfileSearchSilent">profile-service does not answer.</exception>
    Task<ProfileHitPage> SearchAsync(
        SearchFilter filter,
        int pageLength,
        string? cursor,
        CancellationToken cancellationToken = default);

    /// <summary>A single profile, or <c>null</c>.</summary>
    /// <remarks>
    /// For the approach, and likewise <strong>without</strong> a release check
    /// in profile-service: that stands one line above in the handler, so that in
    /// the whole service there is exactly ONE place where visibility is decided.
    /// Two places would be two truths, and their silent variant shows a profile
    /// nobody released.
    /// </remarks>
    /// <exception cref="ProfileSearchSilent">profile-service does not answer.</exception>
    Task<ProfileHit?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
