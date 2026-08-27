using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Verification;

/// <summary>Stores and spends one-time confirmation tokens.</summary>
public interface IVerificationTokenRepository
{
    /// <summary>Records a new token.</summary>
    Task AddAsync(VerificationToken token, CancellationToken cancellationToken = default);

    /// <summary>The token with this hash, if any.</summary>
    /// <remarks>
    /// By hash, because the plaintext exists only in the mail. Nothing here can
    /// hand a link back.
    /// </remarks>
    Task<VerificationToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one token spent.</summary>
    Task ConsumeAsync(Guid id, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>Spends every token a person still holds for one purpose.</summary>
    /// <remarks>
    /// Called before a new one is issued. Without it any number of valid links
    /// stay in circulation at once, and the oldest — possibly the one that went
    /// to the wrong mailbox — keeps working.
    /// </remarks>
    Task ConsumeOpenAsync(
        SubjectId subject,
        TokenPurpose purpose,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);
}
