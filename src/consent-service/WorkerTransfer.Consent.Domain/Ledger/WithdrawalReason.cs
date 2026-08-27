namespace WorkerTransfer.Consent.Domain.Ledger;

/// <summary>A reason that is empty or too long.</summary>
public sealed class InvalidReasonException(string detail) : Exception(detail);

/// <summary>
/// Free text a person wrote about themselves.
/// </summary>
/// <remarks>
/// Its own type rather than a string, because of where it must <em>not</em> go:
/// <c>/check</c> answers without it, an audit row carries it only under the
/// allowlisted key, and account erasure clears it while keeping the rest of the
/// row (ADR-0027 §5). A bare string would have made all three a matter of
/// remembering.
/// </remarks>
public sealed record WithdrawalReason
{
    /// <summary>As much as the boundary contract accepts.</summary>
    public const int HoechsteLaenge = 500;

    private WithdrawalReason(string value) => Value = value;

    /// <summary>What was written.</summary>
    public string Value { get; }

    /// <summary>Reads a reason, or says it cannot.</summary>
    public static bool TryParse(string? value, out WithdrawalReason reason)
    {
        reason = null!;

        if (string.IsNullOrWhiteSpace(value) || value.Length > HoechsteLaenge)
        {
            return false;
        }

        reason = new WithdrawalReason(value);
        return true;
    }

    /// <summary>Reads a reason, or refuses.</summary>
    /// <exception cref="InvalidReasonException">It is empty or too long.</exception>
    public static WithdrawalReason Parse(string value) =>
        TryParse(value, out var reason)
            ? reason
            : throw new InvalidReasonException(
                $"A reason must not be empty and not exceed {HoechsteLaenge} characters.");

    /// <inheritdoc />
    public override string ToString() => Value;
}
