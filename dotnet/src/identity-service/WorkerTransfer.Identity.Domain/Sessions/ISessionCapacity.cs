using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Sessions;

/// <summary>Remembers in what capacity a sign-in is acting.</summary>
/// <remarks>
/// A refresh token carries nothing — it is thirty-two random bytes — and the
/// access token that does carry the company lives fifteen minutes. Without a
/// record of its own, whoever acts for a company silently becomes a private
/// person a quarter of an hour later, in the middle of their work.
/// <para>
/// Keyed by the sign-in rather than by the token: a <see cref="SessionId"/> is
/// stable across the whole rotation chain, so this is one row per sign-in and
/// not one per refresh. Transition debt Ü-5 in
/// <c>docs/uebergang-python-dotnet.md</c> records why it is a table of its own.
/// </para>
/// </remarks>
public interface ISessionCapacity
{
    /// <summary>Records what a sign-in acts as. Replaces any earlier answer.</summary>
    /// <param name="session">The sign-in.</param>
    /// <param name="capacity">What it acts as from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task RememberAsync(
        SessionId session,
        Capacity capacity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a sign-in acts as.
    /// </summary>
    /// <returns>
    /// <see cref="Capacity.AsSelf"/> when nothing was recorded. Acting for
    /// oneself is the default state a person is in, so a missing row is an
    /// answer and not a gap (ADR-0017).
    /// </returns>
    Task<Capacity> RecallAsync(SessionId session, CancellationToken cancellationToken = default);

    /// <summary>Forgets what a sign-in acted as, when the sign-in ends.</summary>
    Task ForgetAsync(SessionId session, CancellationToken cancellationToken = default);
}
