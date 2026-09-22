using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Application.Ports;

/// <summary>The ledger did not answer. No result — a system state.</summary>
/// <remarks>
/// Neither show nor deny: both would be an assertion about the person that
/// nobody can make just now. The endpoint turns it into 503.
/// </remarks>
public sealed class ConsentGateSilent(string reason, Exception? ursache = null)
    : Exception(reason, ursache);

/// <summary>The release keys, built in exactly one place.</summary>
public static class ConsentKey
{
    /// <summary>Whoever may not see the profile may not ask about the market either.</summary>
    /// <remarks>
    /// Otherwise the request would be a channel for learning that this human
    /// being exists here at all.
    /// </remarks>
    public const string Profile = "profile.visibility:public";

    /// <summary>
    /// Always recipient-scoped. There is <strong>no</strong> <c>:public</c>
    /// here.
    /// </summary>
    /// <remarks>
    /// On the profile "to every company" is a sensible choice; here it would be
    /// a switch whose consequences nobody can survey — among them one's own
    /// employer, who is on the same platform.
    /// </remarks>
    public static string Market(TenantId company) => $"market.visibility:tenant:{company}";
}

/// <summary>Reads the ledger and writes into it.</summary>
/// <remarks>
/// Synchronously and without a cache (ADR-0013). There is no field here that
/// could hold an answer, and that is deliberate: a revocation has to take
/// effect at the very next read.
/// </remarks>
public interface IConsentGate
{
    /// <summary>Whether this person's profile is released at all.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MaySeeProfileAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Whether this company may see the market status right now.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task<bool> MaySeeMarketAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>The same question for several pairs, in one go.</summary>
    /// <remarks>
    /// The answers come back <em>in the order of the questions</em>, one per
    /// pair.
    /// </remarks>
    /// <exception cref="ConsentGateSilent">
    /// The ledger did not answer, or answered a different number of times from
    /// the number asked.
    /// </exception>
    Task<IReadOnlyList<bool>> MayEachSeeMarketAsync(
        IReadOnlyList<(SubjectId Subject, TenantId Company)> pairs,
        CancellationToken cancellationToken = default);

    /// <summary>Records the release for this one company.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task GrantAsync(SubjectId subject, TenantId company, CancellationToken cancellationToken = default);

    /// <summary>Takes it back.</summary>
    /// <exception cref="ConsentGateSilent">The ledger did not answer.</exception>
    Task RevokeAsync(
        SubjectId subject, TenantId company, CancellationToken cancellationToken = default);
}
