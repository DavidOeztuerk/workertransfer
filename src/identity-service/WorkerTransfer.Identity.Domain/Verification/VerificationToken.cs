using Girder.Core.Identity;

namespace WorkerTransfer.Identity.Domain.Verification;

/// <summary>What a one-time token is for.</summary>
public enum TokenPurpose
{
    /// <summary>Confirming an address.</summary>
    EmailVerify
}

/// <summary>Maps <see cref="TokenPurpose"/> to the stored value.</summary>
public static class TokenPurposeNames
{
    /// <summary>The stored value for a purpose.</summary>
    public static string ToDatabase(TokenPurpose purpose) => purpose switch
    {
        TokenPurpose.EmailVerify => "email_verify",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown purpose.")
    };

    /// <summary>The purpose for a stored value, or <c>null</c> when it names none.</summary>
    /// <remarks>
    /// Answers <c>null</c> rather than throwing: the value comes from a row a
    /// caller pointed at with a token, and an unknown purpose is a token that
    /// does not apply here — a refusal, not a broken database.
    /// </remarks>
    public static TokenPurpose? FromDatabase(string stored) => stored switch
    {
        "email_verify" => TokenPurpose.EmailVerify,
        _ => null
    };
}

/// <summary>This confirmation link means nothing.</summary>
/// <remarks>
/// Deliberately without detail: unknown and already spent must not be
/// distinguishable from outside, or the endpoint becomes an oracle.
/// </remarks>
public sealed class TokenInvalidException() : Exception("This confirmation link is not valid");

/// <summary>The link is past its time.</summary>
/// <remarks>
/// Its own answer, so the interface can offer to send a new one. It tells
/// nobody anything: whoever holds the token had the mail.
/// </remarks>
public sealed class TokenExpiredException() : Exception("This confirmation link has expired");

/// <summary>A single-use confirmation token, as it is stored.</summary>
/// <remarks>
/// Only the hash is here. A leaked row must not be an account takeover — with
/// the hash alone no link can be built.
/// </remarks>
/// <param name="Id">Which token.</param>
/// <param name="Subject">Whose it is.</param>
/// <param name="TokenHash">SHA-256 of the token. Never the token.</param>
/// <param name="Purpose">What it is for.</param>
/// <param name="ExpiresAt">When it stops working.</param>
/// <param name="ConsumedAt">When it was spent, or <c>null</c>.</param>
public sealed record VerificationToken(
    Guid Id,
    SubjectId Subject,
    string TokenHash,
    TokenPurpose Purpose,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt)
{
    /// <summary>Whether it is past its time.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    /// <summary>Whether it has been spent.</summary>
    public bool IsConsumed => ConsumedAt is not null;
}
