using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Domain.Verification;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>Reads and writes <c>email_verification_tokens</c>.</summary>
public sealed class EfVerificationTokenRepository(IdentityDbContext context, TimeProvider uhr)
    : IVerificationTokenRepository
{
    /// <inheritdoc />
    public Task AddAsync(VerificationToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        context.VerificationTokens.Add(new VerificationTokenRow
        {
            Id = token.Id,
            UserId = token.Subject.Value,
            TokenHash = token.TokenHash,
            Purpose = TokenPurposeNames.ToDatabase(token.Purpose),
            ExpiresAt = token.ExpiresAt.UtcDateTime,
            ConsumedAt = token.ConsumedAt?.UtcDateTime,
            CreatedAt = uhr.GetUtcNow().UtcDateTime
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<VerificationToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        var row = await context.VerificationTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, cancellationToken);

        if (row is null)
        {
            return null;
        }

        // An unknown purpose is a token that does not apply here, not a broken
        // database — so it comes back as no token at all rather than throwing.
        return TokenPurposeNames.FromDatabase(row.Purpose) is not { } zweck
            ? null
            : new VerificationToken(
                row.Id,
                new SubjectId(row.UserId),
                row.TokenHash,
                zweck,
                new DateTimeOffset(row.ExpiresAt, TimeSpan.Zero),
                row.ConsumedAt is { } verbraucht
                    ? new DateTimeOffset(verbraucht, TimeSpan.Zero)
                    : null);
    }

    /// <inheritdoc />
    public async Task ConsumeAsync(
        Guid id,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var row = await context.VerificationTokens
            .AsTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (row is not null)
        {
            row.ConsumedAt = at.UtcDateTime;
        }
    }

    /// <inheritdoc />
    public async Task ConsumeOpenAsync(
        SubjectId subject,
        TokenPurpose purpose,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        var gespeichert = TokenPurposeNames.ToDatabase(purpose);

        var offene = await context.VerificationTokens
            .AsTracking()
            .Where(candidate => candidate.UserId == subject.Value
                                && candidate.Purpose == gespeichert
                                && candidate.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var row in offene)
        {
            row.ConsumedAt = at.UtcDateTime;
        }
    }
}
