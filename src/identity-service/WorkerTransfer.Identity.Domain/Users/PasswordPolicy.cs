namespace WorkerTransfer.Identity.Domain.Users;

/// <summary>A password the policy will not accept.</summary>
public sealed class WeakPasswordException(string reason)
    : Exception($"Password rejected: {reason}")
{
    /// <summary>Why it was refused. For the caller, not for the trail.</summary>
    public string Reason { get; } = reason;
}

/// <summary>The floor a password has to clear.</summary>
/// <remarks>
/// A length floor and a length ceiling, and no composition rules. Demanding a
/// digit and a capital produces <c>Passwort1!</c>, which is shorter in practice
/// than what people choose when simply asked for length.
/// </remarks>
public static class PasswordPolicy
{
    /// <summary>Twelve characters.</summary>
    public const int MinimumCharacters = 12;

    /// <summary>
    /// Seventy-two bytes — bcrypt's limit, not a preference.
    /// </summary>
    /// <remarks>
    /// bcrypt silently ignores everything past the 72nd byte. Accepting a
    /// longer password would mean the tail of it protects nothing while the
    /// person believes it does.
    /// </remarks>
    public const int MaximumBytes = 72;

    /// <exception cref="WeakPasswordException">The password does not clear the floor.</exception>
    public static void Validate(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new WeakPasswordException("must not be empty");
        }

        if (password.Length < MinimumCharacters)
        {
            throw new WeakPasswordException($"must be at least {MinimumCharacters} characters");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(password) > MaximumBytes)
        {
            throw new WeakPasswordException($"exceeds {MaximumBytes} bytes");
        }
    }
}
